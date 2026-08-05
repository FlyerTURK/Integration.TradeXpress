using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Integration.TradeXpress.Mocks.N11;

/// <summary>
/// N11 gibi konuşan yerel uçlar. <b>Uygulama bunlara GERÇEKTEN HTTP ile bağlanır</b> — sınıf takası yoktur;
/// istek kurulumu, kimlik başlıkları, serileştirme, HTTP durum kodları ve ayrıştırma gerçekten koşar.
/// Gerçek/mock geçişi tek config değeridir (<c>N11:Endpoints:BaseUrl</c>), kod yolu AYNIDIR.
///
/// <para><b>Yol adları N11'in kendisiyle birebir</b> olmalıdır — uygulama adresi <c>N11EndpointOptions</c>'tan
/// türetiyor ve taban dışındaki her şey aynı kalıyor.</para>
///
/// <para><b>Kimlik:</b> <c>appkey</c>/<c>appsecret</c> başlıkları ARANIR ama doğrulanmaz — sahte sunucunun işi
/// kimlik denetimi değil. Başlık hiç yoksa 401 döner: gerçek N11 de öyle yapar ve istemcinin başlıkları doğru
/// gönderdiğini kanıtlayan tek şey budur.</para>
/// </summary>
public static class N11MockEndpoints
{
    /// <summary>N11 sahte uçlarını haritalar. Çağıran, ortam ve config kapılarını ZATEN geçmiş olmalıdır.</summary>
    public static IEndpointRouteBuilder MapN11MockEndpoints(
        this IEndpointRouteBuilder endpoints, N11MockStore store, N11MockOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);

        // ⚠ HANDLER'LAR AÇIKÇA TİPLİ (Func<..., Task<IResult>>) — bu bir üslup tercihi DEĞİL, zorunluluk.
        //
        // `MapPost("/x", (HttpContext ctx) => HandlerAsync(ctx))` yazılırsa lambda, MapPost'un RequestDelegate
        // aşırı yüklemesine bağlanır (Task<IResult> → Task dönüşümü geçerlidir). ASP.NET o durumda "yanıtı
        // handler kendisi yazdı" varsayar ve dönen IResult'ı SESSİZCE ATAR: derleme hatası yok, uç eşleşir,
        // log "Executed endpoint" der, istemciye HTTP 200 + BOŞ GÖVDE gider. Canlıda yaşandı (2026-08-05);
        // teşhisi ancak istek log'unda "200 0 null" görülünce mümkün oldu.
        Func<HttpContext, Task<IResult>> create = ctx => SubmitAsync(ctx, store, options, "PRODUCT_CREATE");
        Func<HttpContext, Task<IResult>> update = ctx => SubmitAsync(ctx, store, options, "PRODUCT_UPDATE");
        Func<HttpContext, Task<IResult>> priceStock = ctx => SubmitAsync(ctx, store, options, "PRICE_STOCK_UPDATE");
        Func<HttpContext, Task<IResult>> taskDetails = ctx => TaskDetailsAsync(ctx, store, options);
        Func<HttpContext, Task<IResult>> productQuery = ctx => ProductQueryAsync(ctx, store, options);
        Func<HttpContext, string, Task<IResult>> categoryAttributes =
            (ctx, categoryId) => CategoryAttributesAsync(ctx, categoryId, options);

        // ── Yazma uçları: kuyruğa al, taskId döndür ────────────────────────────────────────────────
        endpoints.MapPost("/ms/product/tasks/product-create", create);
        endpoints.MapPost("/ms/product/tasks/product-update", update);
        endpoints.MapPost("/ms/product/tasks/price-stock-update", priceStock);

        // ── Task durumu ────────────────────────────────────────────────────────────────────────────
        endpoints.MapPost("/ms/product/task-details/page-query", taskDetails);

        // ── Mağaza katalogu ────────────────────────────────────────────────────────────────────────
        endpoints.MapGet("/ms/product-query", productQuery);

        // ── Yaprak kategori nitelikleri (durumsuz sabit set) ───────────────────────────────────────
        endpoints.MapGet("/cdn/category/{categoryId}/attribute", categoryAttributes);

        return endpoints;
    }

    // ── Yazma ───────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> SubmitAsync(
        HttpContext ctx, N11MockStore store, N11MockOptions options, string taskType)
    {
        if (Unauthorized(ctx) is { } denied)
        {
            return denied;
        }

        await DelayAsync(options);

        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        var items = ReadSkus(doc.RootElement);
        var taskId = await store.SubmitAsync(taskType, items);

        // Yazma yanıtı: {id, type, status, reasons[]} — istemcinin ParseSubmission'ının beklediği şekil.
        // status DAİMA IN_QUEUE: gerçek N11 yazma ucunda sonucu ASLA anında vermez.
        return Results.Json(new
        {
            id = taskId,
            type = taskType,
            status = N11MockTaskStates.InQueue,
            reasons = Array.Empty<string>(),
        });
    }

    /// <summary>Gövdeden SKU satırlarını okur: <c>{"payload":{"integrator":…,"skus":[…]}}</c>.
    /// Alanlar SAVUNMACI okunur — üç yazma ucu farklı alan setleri gönderir (create tam, price-stock dar).</summary>
    private static List<N11MockProduct> ReadSkus(JsonElement root)
    {
        var result = new List<N11MockProduct>();
        if (!root.TryGetProperty("payload", out var payload)
            || !payload.TryGetProperty("skus", out var skus)
            || skus.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var sku in skus.EnumerateArray())
        {
            result.Add(new N11MockProduct
            {
                StockCode = Str(sku, "stockCode") ?? string.Empty,
                ProductMainId = Str(sku, "productMainId"),
                Title = Str(sku, "title"),
                SalePrice = Dec(sku, "salePrice"),
                ListPrice = Dec(sku, "listPrice"),
                Quantity = Int(sku, "quantity"),
                CategoryId = Str(sku, "categoryId"),
                ImageUrls = ReadImages(sku),
            });
        }

        return result;
    }

    private static List<string> ReadImages(JsonElement sku)
    {
        var urls = new List<string>();
        if (!sku.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return urls;
        }

        foreach (var image in images.EnumerateArray())
        {
            var url = image.ValueKind == JsonValueKind.String ? image.GetString() : Str(image, "url");
            if (!string.IsNullOrWhiteSpace(url))
            {
                urls.Add(url!);
            }
        }

        return urls;
    }

    // ── Task durumu ─────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> TaskDetailsAsync(HttpContext ctx, N11MockStore store, N11MockOptions options)
    {
        if (Unauthorized(ctx) is { } denied)
        {
            return denied;
        }

        await DelayAsync(options);

        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        var taskId = ReadTaskId(doc.RootElement);
        var task = await store.PollTaskAsync(taskId);
        if (task is null)
        {
            return Results.Json(new { status = "REJECT", reasons = new[] { "Task bulunamadı." } }, statusCode: 404);
        }

        // Yanıt şekli: {status, reasons, skus:{content:[…], last, totalPages}} — poller'ın okuduğu yapı.
        // IN_QUEUE'da skus bloğu BOŞ: task henüz sonuçlanmadı, kalem sonucu yok.
        var content = task.Results.Select(r => new
        {
            itemCode = r.StockCode,
            status = r.Status,
            reasons = r.Reason is null ? Array.Empty<string>() : new[] { r.Reason },
        }).ToList();

        return Results.Json(new
        {
            status = task.Status,
            reasons = Array.Empty<string>(),
            skus = new
            {
                content,
                last = true,
                totalPages = content.Count == 0 ? 0 : 1,
            },
        });
    }

    /// <summary>taskId sayı ya da dizgi gelebilir (istemci ikisini de üretebiliyor) — ikisi de kabul edilir.</summary>
    private static string ReadTaskId(JsonElement root)
    {
        if (!root.TryGetProperty("taskId", out var id))
        {
            return string.Empty;
        }

        return id.ValueKind switch
        {
            JsonValueKind.Number => id.GetInt64().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.String => id.GetString() ?? string.Empty,
            _ => string.Empty,
        };
    }

    // ── Mağaza katalogu ─────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> ProductQueryAsync(HttpContext ctx, N11MockStore store, N11MockOptions options)
    {
        if (Unauthorized(ctx) is { } denied)
        {
            return denied;
        }

        await DelayAsync(options);

        var q = ctx.Request.Query;
        var page = ParseInt(q["page"], 0);
        var size = ParseInt(q["size"], 20);
        var (items, totalPages, totalCount) = await store.QueryProductsAsync(
            page, size, NullIfEmpty(q["stockCode"]), NullIfEmpty(q["productStatus"]));

        // Spring Data Page şekli: content + number + totalPages + totalElements.
        return Results.Json(new
        {
            content = items.Select(p => new
            {
                id = p.N11ProductId,
                productMainId = p.ProductMainId,
                stockCode = p.StockCode,
                title = p.Title,
                salePrice = p.SalePrice,
                listPrice = p.ListPrice,
                quantity = p.Quantity,
                saleStatus = p.SaleStatus,
                status = p.ProductStatus,   // ⚠ yanıtta 'status', istekte 'productStatus' (N11'in kendi asimetrisi)
                categoryId = p.CategoryId,
                imageUrls = p.ImageUrls,
            }).ToList(),
            number = page,
            totalPages,
            totalElements = totalCount,
        });
    }

    // ── Kategori nitelikleri ────────────────────────────────────────────────────────────────────────

    /// <summary>Sabit nitelik seti — durum tutmaz, senaryoya bakmaz. Push doğrulaması niteliklerin
    /// KİMLİKLERİNİ (id + valueId) ister; burada zorunlu OLMAYAN tek varyant ekseni verilir ki en sade
    /// ürün bile geçebilsin.</summary>
    private static async Task<IResult> CategoryAttributesAsync(HttpContext ctx, string categoryId, N11MockOptions options)
    {
        if (Unauthorized(ctx) is { } denied)
        {
            return denied;
        }

        await DelayAsync(options);

        return Results.Json(new
        {
            id = categoryId,
            name = "Mock Kategori " + categoryId,
            categoryAttributes = new[]
            {
                new
                {
                    attributeId = 1001,
                    attributeName = "Renk",
                    isMandatory = false,
                    isVariant = true,
                    isCustomValue = false,
                    priority = 1.0,
                    attributeValues = new[]
                    {
                        new { id = 900001, value = "Sarı" },
                        new { id = 900002, value = "Beyaz" },
                        new { id = 900003, value = "Rose" },
                    },
                },
                new
                {
                    attributeId = 1002,
                    attributeName = "Ayar",
                    isMandatory = false,
                    isVariant = true,
                    isCustomValue = false,
                    priority = 2.0,
                    attributeValues = new[]
                    {
                        new { id = 910014, value = "14" },
                        new { id = 910018, value = "18" },
                        new { id = 910022, value = "22" },
                    },
                },
            },
        });
    }

    // ── Ortak ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Kimlik BAŞLIKLARI var mı — değer doğrulanmaz. Başlık yoksa 401: istemcinin başlıkları
    /// gerçekten gönderdiğini kanıtlayan tek denetim budur (sınıf takasında bu adım hiç koşmazdı).</summary>
    private static IResult? Unauthorized(HttpContext ctx)
    {
        var hasKey = ctx.Request.Headers.ContainsKey("appkey");
        var hasSecret = ctx.Request.Headers.ContainsKey("appsecret");
        if (hasKey && hasSecret)
        {
            return null;
        }

        return Results.Json(
            new { message = "Apide doğrulama işlemi başarısız oldu.", description = "appkey/appsecret başlığı yok." },
            statusCode: 401);
    }

    private static Task DelayAsync(N11MockOptions options)
    {
        return options.LatencyMs > 0 ? Task.Delay(options.LatencyMs) : Task.CompletedTask;
    }

    private static string? Str(JsonElement e, string name)
    {
        return e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static decimal? Dec(JsonElement e, string name)
    {
        return e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
    }

    private static int? Int(JsonElement e, string name)
    {
        return e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
    }

    private static int ParseInt(string? raw, int fallback)
    {
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static string? NullIfEmpty(string? raw)
    {
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }
}
