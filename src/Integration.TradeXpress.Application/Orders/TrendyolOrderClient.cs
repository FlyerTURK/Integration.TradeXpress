using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Trendyol;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// <see cref="ITrendyolOrderClient"/> — Trendyol Marketplace API v2 sipariş OKUMA istemcisi. Auth + gönderim ORTAK
/// TABANDAN (<see cref="TrendyolRestClientBase"/>; ürün/kategori istemcileriyle aynı). Salt GET.
/// <para><b>Endpoint CANLI DOĞRULANDI (2026-07-11):</b> <c>/integration/order/sellers/{sellerId}/orders</c> → HTTP 200.
/// <c>/shipment-packages</c> aynı kimlikle <b>401</b> döndürüyordu (yanlış path). Yanıt zarfı sayfalıdır
/// (content[]/totalPages); kalem alan adları <see cref="ParseShipmentPackagesPage"/>'de defansif okunur.</para>
/// </summary>
public sealed class TrendyolOrderClient : TrendyolRestClientBase, ITrendyolOrderClient, ITransientDependency
{
    /// <summary>Sayfalama döngüsü güvenlik tavanı — totalPages bozuk/aşırı dönerse sonsuz döngüye girilmez.</summary>
    private const int MaxPageLoops = 500;

    /// <summary>Trendyol sipariş ucu <c>startDate</c>/<c>endDate</c> aralığını EN FAZLA 2 hafta kabul eder (canlı
    /// doğrulandı) → tüm geçmiş 14 günlük ardışık pencerelere bölünerek çekilir.</summary>
    private const int WindowDays = 14;

    public async Task<TrendyolOrdersPage> GetOrdersPageAsync(
        TrendyolCredentials credentials, long startDateEpochMs, long endDateEpochMs, int page, int size, CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/integration/order/sellers/{credentials.SellerId}/orders" +
                  $"?startDate={startDateEpochMs}&endDate={endDateEpochMs}&page={page}&size={size}";

        var (ok, status, payload) = await SendGetWithRetryAsync(url, credentials, cancellationToken);
        if (!ok)
        {
            throw new BusinessException("TradeXpress:Trendyol:Order:ListFailed")
                .WithData("status", status)
                .WithData("body", Truncate(payload));
        }

        return ParseShipmentPackagesPage(page, size, payload);
    }

    public async Task<IReadOnlyList<RemoteOrder>> GetAllOrdersAsync(
        TrendyolCredentials credentials, DateTime sinceUtc, int pageSize = 200, CancellationToken cancellationToken = default)
    {
        return await FetchAllWindowsAsync(
            sinceUtc,
            DateTime.UtcNow,
            (startMs, endMs) => FetchAllPagesAsync(
                page => GetOrdersPageAsync(credentials, startMs, endMs, page, pageSize, cancellationToken)));
    }

    /// <summary>Tarih-penceresi döngüsü (public static — birim test edilir; pencere kaynağı delege): <paramref name="sinceUtc"/>'den
    /// <paramref name="nowUtc"/>'ye kadar ardışık 14 günlük pencerelere böler, her pencereyi <paramref name="fetchWindow"/>
    /// (epoch-ms start, epoch-ms end) ile çeker ve birleştirir. Pencereler oluşturma-tarihine göre ayrık → dublike yok
    /// (ayrıca upsert RemoteOrderId ile idempotent). Trendyol'un varsayılan "yalnız son ~2 hafta" davranışını aşar
    /// (sessiz kapsam düşürme yasak).</summary>
    public static async Task<List<RemoteOrder>> FetchAllWindowsAsync(
        DateTime sinceUtc, DateTime nowUtc, Func<long, long, Task<List<RemoteOrder>>> fetchWindow)
    {
        var all = new List<RemoteOrder>();
        var windowStart = sinceUtc;
        while (windowStart < nowUtc)
        {
            var windowEnd = windowStart.AddDays(WindowDays);
            if (windowEnd > nowUtc)
            {
                windowEnd = nowUtc;
            }

            all.AddRange(await fetchWindow(ToEpochMs(windowStart), ToEpochMs(windowEnd)));
            windowStart = windowEnd;
        }

        return all;
    }

    private static long ToEpochMs(DateTime utc)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    }

    /// <summary>Sayfalama döngüsü (public static — birim test edilir; sayfa kaynağı delege): totalPages'e kadar sırayla
    /// çeker (ürün istemcisinin sayfalama deseni). Güvenlik tavanı aşılırsa SESSİZ kısmî liste yerine dostane hata —
    /// çekim salt-okuma/upsert-only olduğundan yeniden deneme güvenlidir (sessiz kapsam düşürme yasak).</summary>
    public static async Task<List<RemoteOrder>> FetchAllPagesAsync(Func<int, Task<TrendyolOrdersPage>> fetchPage)
    {
        var all = new List<RemoteOrder>();
        var page = 0;
        int totalPages;
        do
        {
            var result = await fetchPage(page);
            all.AddRange(result.Items);
            totalPages = result.TotalPages;
            page++;
        }
        while (page < totalPages && page < MaxPageLoops);

        if (page < totalPages)
        {
            throw new BusinessException("TradeXpress:Trendyol:Order:PageLimitExceeded")
                .WithData("totalPages", totalPages)
                .WithData("maxPages", MaxPageLoops);
        }

        return all;
    }

    /// <summary>Sevkiyat paketleri yanıtını parse eder (public static — birim test edilir): sayfalama zarfı +
    /// <c>content[]</c> paketleri. Alan adları defansif okunur (sayı/metin id toleransı; orderDate epoch-ms → UTC;
    /// müşteri adı ad+soyad birleşimi; tutar grossAmount ?? totalPrice ?? satır toplamı). Bozuk JSON body'si dostane
    /// hatayla ÇEKİMİ DURDURUR (sessizce boş sayfa dönmek o ve sonraki sayfaları raporsuz kaybettirirdi).</summary>
    public static TrendyolOrdersPage ParseShipmentPackagesPage(int page, int size, string payload)
    {
        var items = new List<RemoteOrder>();
        int totalPages;
        long totalElements;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            totalPages = ReadInt(root, "totalPages") ?? 0;
            totalElements = ReadLong(root, "totalElements") ?? 0;

            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in content.EnumerateArray())
                {
                    items.Add(ReadOrder(el));
                }
            }
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Trendyol:Order:ListParseFailed")
                .WithData("page", page);
        }

        return new TrendyolOrdersPage(page, size, totalPages, totalElements, items);
    }

    private static RemoteOrder ReadOrder(JsonElement el)
    {
        // İdempotency anahtarı: shipmentPackageId ?? id ?? orderNumber (biri MUTLAKA olmalı — yoksa boş kalır,
        // upsert tarafı boş anahtarlı kaydı atlar+raporlar).
        var remoteOrderId = ReadIdAsString(el, "shipmentPackageId")
                            ?? ReadIdAsString(el, "id")
                            ?? ReadIdAsString(el, "orderNumber")
                            ?? string.Empty;
        var orderNumber = ReadString(el, "orderNumber") ?? remoteOrderId;

        var lines = new List<RemoteOrderLine>();
        if (el.TryGetProperty("lines", out var linesEl) && linesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in linesEl.EnumerateArray())
            {
                lines.Add(ReadLine(line));
            }
        }

        var totalAmount = ReadDecimal(el, "grossAmount")
                          ?? ReadDecimal(el, "totalPrice")
                          ?? SumLines(lines);

        return new RemoteOrder(
            RemoteOrderId: remoteOrderId,
            OrderNumber: orderNumber,
            OrderDate: ReadEpochMillisAsUtc(el, "orderDate") ?? DateTime.UtcNow,
            RemoteStatus: ReadString(el, "shipmentPackageStatus") ?? ReadString(el, "status"),
            CustomerName: BuildCustomerName(el),
            TotalAmount: totalAmount,
            CargoProvider: ReadString(el, "cargoProviderName") ?? ReadString(el, "cargoProvider"),
            CargoTrackingNumber: ReadIdAsString(el, "cargoTrackingNumber"),
            Lines: lines);
    }

    private static RemoteOrderLine ReadLine(JsonElement el)
    {
        var quantity = ReadDecimal(el, "quantity") ?? 0m;
        var unitPrice = ReadDecimal(el, "price") ?? ReadDecimal(el, "amount") ?? 0m;
        var lineTotal = ReadDecimal(el, "totalPrice") ?? (quantity * unitPrice);

        return new RemoteOrderLine(
            RemoteLineId: ReadIdAsString(el, "id") ?? ReadIdAsString(el, "orderLineId"),
            Barcode: ReadString(el, "barcode"),
            StockCode: ReadString(el, "merchantSku") ?? ReadString(el, "stockCode") ?? ReadString(el, "sku"),
            ProductName: ReadString(el, "productName") ?? ReadString(el, "productTitle") ?? string.Empty,
            Quantity: quantity,
            UnitPrice: unitPrice,
            LineTotal: lineTotal,
            RemoteLineStatus: ReadString(el, "orderLineItemStatusName") ?? ReadString(el, "status"));
    }

    private static decimal SumLines(IReadOnlyList<RemoteOrderLine> lines)
    {
        var sum = 0m;
        foreach (var line in lines)
        {
            sum += line.LineTotal;
        }

        return sum;
    }

    private static string? BuildCustomerName(JsonElement el)
    {
        var first = ReadString(el, "customerFirstName");
        var last = ReadString(el, "customerLastName");
        var full = ReadString(el, "customerFullName") ?? $"{first} {last}".Trim();
        return string.IsNullOrWhiteSpace(full) ? null : full;
    }

    // ── Defansif JSON okuyucular (ürün istemcisiyle aynı toleranslar) ─────────────────────────────────

    private static string? ReadString(JsonElement obj, string property)
    {
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String)
        {
            var value = el.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static string? ReadIdAsString(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.String when !string.IsNullOrWhiteSpace(el.GetString()) => el.GetString(),
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null,
        };
    }

    private static long? ReadLong(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt64(out var l) => l,
            JsonValueKind.String when long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
            _ => null,
        };
    }

    private static decimal? ReadDecimal(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDecimal(out var d) => d,
            JsonValueKind.String when decimal.TryParse(el.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    /// <summary>Trendyol tarih alanları epoch-milisaniye (UTC). Sayı ya da metin gelebilir → UTC DateTime.</summary>
    private static DateTime? ReadEpochMillisAsUtc(JsonElement obj, string property)
    {
        var millis = ReadLong(obj, property);
        if (millis is null)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(millis.Value).UtcDateTime;
    }

    private static string Truncate(string value)
    {
        return value.Length <= 500 ? value : value.Substring(0, 500);
    }
}
