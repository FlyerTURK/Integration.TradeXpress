using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannels.Etsy;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// <see cref="IEtsyOrderClient"/> — Etsy Open API v3 receipt OKUMA istemcisi. Salt GET (<c>getShopReceipts</c>). Auth:
/// access token <see cref="IEtsyTokenProvider"/>'dan (refresh şeffaf), <c>x-api-key</c> = birleşik {keystring}:{secret}
/// (EtsyOAuthClient ile aynı gereklilik). Para tutarları Etsy money-object (<c>{amount,divisor}</c>) → decimal; tarih
/// alanları epoch-SANİYE (Trendyol epoch-ms'nin aksine). Defansif JSON okuma (alan yoksa/tipi farklıysa null).
/// </summary>
public sealed class EtsyOrderClient : IEtsyOrderClient, ITransientDependency
{
    // Sipariş çekimi seyrek → paylaşılan tek HttpClient (OAuth istemcisiyle aynı desen).
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const int MaxPageLoops = 500;   // offset döngüsü güvenlik tavanı (bozuk count → sonsuz döngü olmasın)

    private readonly IEtsyTokenProvider _tokenProvider;

    public EtsyOrderClient(IEtsyTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    public async Task<IReadOnlyList<RemoteOrder>> GetAllOrdersAsync(
        EtsyCredentials credentials, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(credentials.ChannelId, cancellationToken);

        var all = new List<RemoteOrder>();
        var offset = 0;
        var loops = 0;
        int count;
        do
        {
            var (items, total) = await GetReceiptsPageAsync(credentials, accessToken, offset, pageSize, cancellationToken);
            all.AddRange(items);
            count = total;
            offset += pageSize;
            loops++;
        }
        while (offset < count && loops < MaxPageLoops);

        if (offset < count)
        {
            throw new BusinessException("TradeXpress:Etsy:Order:PageLimitExceeded")
                .WithData("count", count)
                .WithData("maxPages", MaxPageLoops);
        }

        return all;
    }

    private async Task<(List<RemoteOrder> Items, int Count)> GetReceiptsPageAsync(
        EtsyCredentials credentials, string accessToken, int offset, int limit, CancellationToken cancellationToken)
    {
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/shops/{Uri.EscapeDataString(credentials.ShopId)}/receipts" +
                  $"?limit={limit}&offset={offset}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("x-api-key", credentials.ApiKeyHeader);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException("TradeXpress:Etsy:Order:ListFailed")
                .WithData("status", (int)response.StatusCode)
                .WithData("body", Truncate(payload));
        }

        return ParseReceiptsPage(payload);
    }

    /// <summary>Receipts yanıtını parse eder (public static — birim testli): <c>count</c> + <c>results[]</c>. Bozuk JSON
    /// body'si dostane hatayla ÇEKİMİ DURDURUR (sessizce boş sayfa dönmek o ve sonraki sayfaları raporsuz kaybettirirdi).</summary>
    public static (List<RemoteOrder> Items, int Count) ParseReceiptsPage(string payload)
    {
        var items = new List<RemoteOrder>();
        int count;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            count = ReadInt(root, "count") ?? 0;

            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in results.EnumerateArray())
                {
                    items.Add(ReadReceipt(el));
                }
            }
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Etsy:Order:ListParseFailed");
        }

        return (items, count);
    }

    private static RemoteOrder ReadReceipt(JsonElement el)
    {
        var remoteOrderId = ReadIdAsString(el, "receipt_id") ?? string.Empty;

        var lines = new List<RemoteOrderLine>();
        if (el.TryGetProperty("transactions", out var txEl) && txEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tx in txEl.EnumerateArray())
            {
                lines.Add(ReadTransaction(tx));
            }
        }

        var (total, currencyCode) = ReadMoney(el, "grandtotal");

        return new RemoteOrder(
            RemoteOrderId: remoteOrderId,
            OrderNumber: ReadString(el, "receipt_id") ?? remoteOrderId,
            OrderDate: ReadEpochSecondsAsUtc(el, "created_timestamp") ?? DateTime.UtcNow,
            RemoteStatus: ReadString(el, "status"),
            CustomerName: ReadString(el, "name"),
            TotalAmount: total ?? SumLines(lines),
            CargoProvider: ReadFirstShipmentField(el, "carrier_name"),
            CargoTrackingNumber: ReadFirstShipmentField(el, "tracking_code"),
            Lines: lines,
            CurrencyCode: currencyCode);
    }

    private static RemoteOrderLine ReadTransaction(JsonElement el)
    {
        var quantity = ReadDecimal(el, "quantity") ?? 0m;
        var (unitPrice, _) = ReadMoney(el, "price");
        var effectiveUnit = unitPrice ?? 0m;

        return new RemoteOrderLine(
            RemoteLineId: ReadIdAsString(el, "transaction_id"),
            Barcode: null,   // Etsy'de barkod kavramı yok
            StockCode: ReadString(el, "sku"),
            ProductName: ReadString(el, "title") ?? string.Empty,
            Quantity: quantity,
            UnitPrice: effectiveUnit,
            LineTotal: quantity * effectiveUnit,
            RemoteLineStatus: null);   // Etsy'de kalem-başı durum yok (receipt seviyesi)
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

    // ── Etsy-özel okuyucular ──────────────────────────────────────────────────────────────────────────

    /// <summary>Etsy money-object: <c>{amount, divisor, currency_code}</c> → (amount/divisor, currency_code). Alan yoksa
    /// (null, null). divisor 0/yoksa güvenli 1 (bölme hatası olmasın).</summary>
    private static (decimal? Value, string? CurrencyCode) ReadMoney(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var money)
            || money.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        var amount = ReadLong(money, "amount");
        if (amount is null)
        {
            return (null, ReadString(money, "currency_code"));
        }

        var divisor = ReadLong(money, "divisor");
        var effectiveDivisor = divisor is null or 0 ? 1L : divisor.Value;
        return ((decimal)amount.Value / effectiveDivisor, ReadString(money, "currency_code"));
    }

    // İlk sevkiyat (shipments[0]) alanını okur (kargo firması / takip no) — yoksa null.
    private static string? ReadFirstShipmentField(JsonElement el, string field)
    {
        if (!el.TryGetProperty("shipments", out var shipments) || shipments.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var shipment in shipments.EnumerateArray())
        {
            var value = ReadString(shipment, field);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Etsy tarih alanları epoch-SANİYE (UTC). Sayı ya da metin gelebilir → UTC DateTime.</summary>
    private static DateTime? ReadEpochSecondsAsUtc(JsonElement obj, string property)
    {
        var seconds = ReadLong(obj, property);
        if (seconds is null)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds.Value).UtcDateTime;
    }

    // ── Defansif JSON okuyucular (sipariş istemcileriyle aynı toleranslar) ────────────────────────────

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

    private static string Truncate(string value)
    {
        return value.Length <= 500 ? value : value.Substring(0, 500);
    }
}
