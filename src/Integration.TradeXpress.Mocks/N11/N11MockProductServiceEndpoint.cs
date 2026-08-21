using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Integration.TradeXpress.Mocks.N11;

/// <summary>
/// N11 SOAP <c>ProductService</c> (soru-cevap) ve <c>CategoryService</c> (kimlik probu) taklitleri.
///
/// <para><b>Soru-cevap:</b> uygulamanın <c>ChannelQuestions</c> dilimi yazıldı ama hiç doğrulanmadı. Burada
/// yalnız OKUMA uçları taklit ediliyor (<c>GetProductQuestionList</c> · <c>GetProductQuestionDetail</c>) —
/// <c>SaveProductAnswer</c> BİLEREK yok: cevabın kanala gönderilmesi 2026-08-01'de kullanıcı tarafından
/// ertelendi ve uygulamada da çağıranı yok. Olmayan bir yeteneği mock'lamak yanlış izlenim yaratırdı.</para>
///
/// <para><b>Kimlik probu:</b> <c>GetTopLevelCategories</c>. Kanal OLUŞTURULURKEN doğrulayıcı bu ucu çağırıyor;
/// mock'ta cevap verilmezse yeni N11 kanalı (ve dolayısıyla kurulum sihirbazı) hiç denenemez. Kategori AĞACI
/// için kullanılmıyor — ağaç zaten yerel DB'de; burada tek amaç "kimlik geçerli mi" sorusuna cevap vermek.</para>
///
/// <para><b>Başarı sözleşmesi soruda FARKLI:</b> istemci <c>status</c>'ü kökten değil <c>result</c> bloğundan
/// okuyor — çünkü soru DETAYI da <c>status</c> adında bir alan taşıyor (sorunun kendi durumu) ve kör arama
/// yanlış elemanı yakalıyor. Mock bu iç içeliği aynen üretmek zorunda.</para>
/// </summary>
public static class N11MockProductServiceEndpoint
{
    private const string SoapNs = "http://schemas.xmlsoap.org/soap/envelope/";

    public static IEndpointRouteBuilder MapN11MockProductServiceEndpoint(
        this IEndpointRouteBuilder endpoints, N11MockStore store, N11MockOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(store);

        // Açık tip ZORUNLU — RequestDelegate tuzağı (bkz. N11MockEndpoints).
        Func<HttpContext, Task<IResult>> product = ctx => HandleProductAsync(ctx, store, options);
        Func<HttpContext, Task<IResult>> category = _ => Task.FromResult(Soap(TopLevelCategories()));

        endpoints.MapPost("/ws/ProductService.wsdl", product);
        endpoints.MapPost("/ws/CategoryService.wsdl", category);
        return endpoints;
    }

    private static async Task<IResult> HandleProductAsync(HttpContext ctx, N11MockStore store, N11MockOptions options)
    {
        if (options.LatencyMs > 0)
        {
            await Task.Delay(options.LatencyMs);
        }

        var raw = await new StreamReader(ctx.Request.Body, Encoding.UTF8).ReadToEndAsync();
        XDocument request;
        try
        {
            request = XDocument.Parse(raw);
        }
        catch (System.Xml.XmlException)
        {
            return Soap(QuestionFault("İstek gövdesi geçerli XML değil."));
        }

        var operation = request.Root?.Descendants()
            .FirstOrDefault(e => e.Name.LocalName.EndsWith("Request", StringComparison.Ordinal))?
            .Name.LocalName ?? string.Empty;

        return operation switch
        {
            "GetProductQuestionListRequest" => Soap(await QuestionListAsync(store)),
            "GetProductQuestionDetailRequest" => Soap(await QuestionDetailAsync(store, request)),
            _ => Soap(QuestionFault($"Tanınmayan operasyon: '{operation}'.")),
        };
    }

    // ── Soru listesi ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Mağazadaki her ürün için bir soru üretir — sipariş taklidiyle aynı ilke: senaryo tek yerden
    /// (ürün deposu) kurulur, ayrı bir soru deposu tutulmaz.</summary>
    private static async Task<XElement> QuestionListAsync(N11MockStore store)
    {
        var (products, _, _) = await store.QueryProductsAsync(0, 200, null, null);

        return new XElement("GetProductQuestionListResponse",
            new XElement("result", new XElement("status", "success")),
            new XElement("pagingData",
                new XElement("pageCount", 1),
                new XElement("totalCount", products.Count)),
            new XElement("productQuestions",
                products.Select((p, i) => new XElement("productQuestion",
                    new XElement("id", 7000000000L + i),
                    new XElement("productId", p.N11ProductId),
                    new XElement("productTitle", p.Title ?? p.StockCode),
                    new XElement("questionSubject", "Ürün hakkında"),
                    new XElement("question", $"{p.Title ?? p.StockCode} kaç gram, sertifikalı mı?"),
                    new XElement("answer", string.Empty))).ToArray()));
    }

    private static async Task<XElement> QuestionDetailAsync(N11MockStore store, XDocument request)
    {
        var requestedId = request.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "productQuestionId")?.Value.Trim();

        var (products, _, _) = await store.QueryProductsAsync(0, 200, null, null);
        if (products.Count == 0)
        {
            return QuestionFault("Soru bulunamadı (sahte mağaza boş).");
        }

        var index = 0;
        if (long.TryParse(requestedId, out var id))
        {
            index = (int)Math.Clamp(id - 7000000000L, 0, products.Count - 1);
        }

        var product = products[index];

        // ⚠ status HEM result bloğunda (işlem sonucu) HEM soru içinde (sorunun durumu) var — istemci ikisini
        // ayırt edebilmek için status'ü result'tan okuyor. Mock bu iç içeliği aynen üretmezse ayrım kaybolur.
        return new XElement("GetProductQuestionDetailResponse",
            new XElement("result", new XElement("status", "success")),
            new XElement("productQuestion",
                new XElement("productId", product.N11ProductId),
                new XElement("productTitle", product.Title ?? product.StockCode),
                new XElement("questionSubject", "Ürün hakkında"),
                new XElement("question", $"{product.Title ?? product.StockCode} kaç gram, sertifikalı mı?"),
                new XElement("answer", string.Empty),
                new XElement("questionDate", "01/08/2026"),
                new XElement("status", "OPEN"),
                new XElement("fullName", "Mock Müşteri"),
                new XElement("email", "musteri@example.invalid"),
                new XElement("buyerExpose", "true")));
    }

    // ── Kimlik probu ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Doğrulayıcı yalnız HTTP 200 + yanıt body'sinde açık <c>status=failure</c> OLMAMASINI arıyor.
    /// Ağaç içeriği önemsiz — kategori ağacı yerel DB'den okunuyor.</summary>
    private static XElement TopLevelCategories()
    {
        return new XElement("GetTopLevelCategoriesResponse",
            new XElement("result", new XElement("status", "success")),
            new XElement("categoryList",
                new XElement("category",
                    new XElement("id", 1000),
                    new XElement("name", "Mock Üst Kategori"))));
    }

    // ── Zarf ────────────────────────────────────────────────────────────────────────────────────────

    private static XElement QuestionFault(string message)
    {
        return new XElement("ErrorResponse",
            new XElement("result",
                new XElement("status", "failure"),
                new XElement("errorMessage", message)));
    }

    private static IResult Soap(XElement body)
    {
        XNamespace ns = SoapNs;
        var envelope = new XDocument(
            new XElement(ns + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", SoapNs),
                new XElement(ns + "Body", body)));

        return Results.Content(envelope.ToString(), "text/xml; charset=utf-8");
    }
}
