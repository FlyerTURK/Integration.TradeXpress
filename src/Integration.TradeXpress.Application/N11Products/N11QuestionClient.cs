using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Integration.TradeXpress.ChannelQuestions;
using Integration.TradeXpress.SalesChannels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// <see cref="IChannelQuestionClient"/>'ın N11 uygulaması — SOAP <c>ProductService.GetProductQuestionList</c> /
/// <c>GetProductQuestionDetail</c> (SALT-OKUMA; <c>SaveProductAnswer</c> bu dilimde HİÇ çağrılmaz). Zarf/auth/gönderim
/// deseni <see cref="N11ProductClient"/> ile birebir aynıdır (prefix'li wrapper + unqualified children +
/// <c>appkey</c>/<c>appsecret</c> HTTP başlıkları); yanıt namespace/sıra AGNOSTİK parse edilir. Sır loglanmaz.
///
/// <para><b>Kota (canlı keşif 2026-08-01):</b> N11 bu ucu <b>dakikada 1 çağrıya</b> kısar ve kota TÜM hesap için
/// ortaktır — paralellik aşmaz (3 eşzamanlıdan 2'si <c>accessLimit</c> aldı). Bu yüzden istemcide sayfa döngüsü
/// YOKTUR (kuyruk yönetir) ve kota hatası İSTİSNAYA ÇEVRİLMEZ: <see cref="RemoteQuestionPage.RateLimited"/>
/// işaretli boş sayfa döner.</para>
///
/// <para><b>Liste/detay asimetrisi:</b> liste öğesinde tarih/durum/müşteri YOKTUR, detay yanıtında ise <c>id</c>
/// YOKTUR — eşleme kimliği İSTEKTEN taşınır (<see cref="ParseDetailResponse"/>).</para>
///
/// <para><b>Doğrulama durumu:</b> endpoint/auth/zarf/sayfalama CANLI doğrulandı, ama keşif anında hesapta 0 soru
/// vardı → gerçek soru body'si (özellikle <c>images</c> ve <c>questionDate</c> biçimi) GÖRÜLMEDİ; alan adları
/// WSDL'den alındı. İlk gerçek soru geldiğinde ayrıştırma doğrulanmalıdır.</para>
/// </summary>
[ExposeServices(typeof(IChannelQuestionClient))]
public sealed class N11QuestionClient : IChannelQuestionClient, ITransientDependency
{
    // Sorular ÜRÜN servisinin altındadır — yeni endpoint/anlaşma YOK (canlı doğrulandı). Aynı sabit
    // N11ProductClient.Endpoint'te de yaşıyor (o sınıfın private'ı); tek-kaynağa çekmek ayrı bir temizlik işi.
    private static readonly XNamespace Soapenv = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Sch = "http://www.n11.com/ws/schemas";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    // N11 ARAMA FİLTRESİNİN kabul ettiği tek iki değer (WSDL ProductQuestionStatus). Detaydaki `status` alanı
    // bunlarla SINIRLI DEĞİLDİR — o serbest metindir ve senkron katmanında tolerant eşlenir.
    private const string OpenStatus = "OPEN";
    private const string ClosedStatus = "CLOSED";

    /// <summary>N11 tarih biçimi (canlı doğrulandı: aralık hatası döndü, format hatası değil).</summary>
    private const string N11DateFormat = "dd/MM/yyyy";

    /// <summary>Kota duvarının imzası: <c>SELLER_API.getProductQuestionListRequest.accessLimit.reached</c>.
    /// Tam koda değil, bu parçaya bakılır — N11 hata kodunun ön ekini uç bazında değiştirir.</summary>
    private const string AccessLimitMarker = "accessLimit";

    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly ILogger<N11QuestionClient> _logger;

    // Uç adresi N11EndpointOptions'tan gelir (varsayılan https://api.n11.com).
    private readonly N11EndpointOptions _endpoints;

    private string Endpoint
    {
        get { return _endpoints.ProductServiceEndpoint; }
    }

    public N11QuestionClient(
        IRepository<SalesChannelTrN11, Guid> channelRepository,
        ILogger<N11QuestionClient> logger,
        IOptions<N11EndpointOptions> endpointOptions)
    {
        _channelRepository = channelRepository;
        _logger = logger;
        _endpoints = endpointOptions.Value;
    }

    public SalesChannelType ChannelType
    {
        get
        {
            return SalesChannelType.TrN11;
        }
    }

    // ── Çekim uçları ─────────────────────────────────────────────────────────────────────────────────

    public async Task<RemoteQuestionPage> FetchPageAsync(
        Guid salesChannelId, ChannelQuestionQuery query, CancellationToken cancellationToken = default)
    {
        var credentials = await ResolveCredentialsAsync(salesChannelId, cancellationToken);
        var request = BuildListRequest(credentials.AppKey, credentials.AppSecret, query);
        var response = await PostAsync(
            request, credentials.AppKey, credentials.AppSecret, "TradeXpress:N11:Question:ListFailed", cancellationToken);

        var page = ParseListResponse(response);
        if (page.RateLimited)
        {
            // BİLGİ seviyesi (uyarı DEĞİL): bu beklenen bir durum — dakikada 1 çağrı kuralının normal işleyişi.
            _logger.LogInformation(
                "N11 soru listesi kota duvarına takıldı (kanal {ChannelId}, sayfa {Page}) — bir sonraki tura ertelendi.",
                salesChannelId, query.PageIndex);
        }

        return page;
    }

    public async Task<RemoteQuestion?> FetchDetailAsync(
        Guid salesChannelId, string remoteQuestionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteQuestionId))
        {
            return null;
        }

        var credentials = await ResolveCredentialsAsync(salesChannelId, cancellationToken);
        var request = new XElement(Sch + "GetProductQuestionDetailRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            Auth(credentials.AppKey, credentials.AppSecret),
            new XElement("productQuestionId", remoteQuestionId.Trim()));

        var response = await PostAsync(
            request, credentials.AppKey, credentials.AppSecret, "TradeXpress:N11:Question:DetailFailed", cancellationToken);

        // Kota duvarı detay ucunda da istisna DEĞİL (liste ucuyla aynı felsefe): null döner, kuyruk yeniden dener.
        // Detay dönüş tipi tek bir kayıttır → RateLimited taşıyacak bir alan yoktur; ayrımı log taşır.
        if (IsAccessLimit(response))
        {
            _logger.LogInformation(
                "N11 soru detayı kota duvarına takıldı (kanal {ChannelId}, soru {QuestionId}) — bir sonraki tura ertelendi.",
                salesChannelId, remoteQuestionId);
            return null;
        }

        return ParseDetailResponse(response, remoteQuestionId.Trim());
    }

    // ── İstek kurma ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>GetProductQuestionList isteği (WSDL: auth + productQuestionSearch + pagingData) — saf, testlenebilir
    /// (ağ yok; kimlik parametreleri yalnız zarfa yazılır).
    /// <para>Arama bloğuna YALNIZ elimizdeki alanlar konur: WSDL <c>productId</c>/<c>buyerEmail</c>/<c>subject</c>/
    /// <c>questionDate</c> alanlarını da tanımlar ama canlı çağrı bunlar olmadan çalışır — göndermemek "hepsi"
    /// demektir, boş string göndermek ise filtreyi bozabilir.</para></summary>
    public static XElement BuildListRequest(string appKey, string appSecret, ChannelQuestionQuery query)
    {
        var search = new XElement("productQuestionSearch",
            new XElement("status", query.OnlyOpen ? OpenStatus : ClosedStatus));

        if (!query.OnlyOpen)
        {
            // CLOSED'da startDate ZORUNLU (canlı: SELLER_API.nullParam). Yerelde yakalanır: N11'e gitmeyen bir
            // hata dakikalık kotayı YEMEZ — kota duvarının altında her boşa giden çağrı bir tur kaybıdır.
            if (query.StartDate is not { } startDate)
            {
                throw new BusinessException("TradeXpress:N11:Question:StartDateRequired");
            }

            search.Add(new XElement("startDate", FormatDate(startDate)));
            if (query.EndDate is { } endDate)
            {
                search.Add(new XElement("endDate", FormatDate(endDate)));
            }
        }

        return new XElement(Sch + "GetProductQuestionListRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            Auth(appKey, appSecret),
            search,
            // currentPage 0-TABANLI (canlı doğrulandı); pageSize=100 kabul ediliyor.
            new XElement("pagingData",
                new XElement("currentPage", query.PageIndex.ToString(CultureInfo.InvariantCulture)),
                new XElement("pageSize", query.PageSize.ToString(CultureInfo.InvariantCulture))));
    }

    /// <summary>İş tarihini N11 biçimine çevirir. Timezone kaydırması YOK: bu alan gün hassasiyetli bir İŞ
    /// TARİHİDİR, timestamp değil (kaydırmak gün kaymasına yol açardı).</summary>
    private static string FormatDate(DateTime value)
    {
        return value.ToString(N11DateFormat, CultureInfo.InvariantCulture);
    }

    private static XElement Auth(string appKey, string appSecret)
    {
        return new XElement("auth", new XElement("appKey", appKey), new XElement("appSecret", appSecret));
    }

    // ── Parse (testlenebilir saf statik — ağ yok) ────────────────────────────────────────────────────

    /// <summary>GetProductQuestionListResponse → kanal-agnostik sayfa.
    /// <para>Kota duvarı (<c>accessLimit</c>) İSTİSNA DEĞİL: <see cref="RemoteQuestionPage.FromRateLimit"/> döner.
    /// Diğer <c>result/status != success</c> hâlleri dostane <c>BusinessException</c>'dır.</para></summary>
    public static RemoteQuestionPage ParseListResponse(XDocument doc)
    {
        if (IsAccessLimit(doc))
        {
            return RemoteQuestionPage.FromRateLimit();
        }

        EnsureSuccess(doc, "TradeXpress:N11:Question:ListRejected");

        var paging = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "pagingData");
        return new RemoteQuestionPage(
            ParseListItems(doc),
            ParseInt(Local(paging, "totalCount")) ?? 0,
            ParseInt(Local(paging, "pageCount")) ?? 0,
            RateLimited: false);
    }

    /// <summary>GetProductQuestionDetailResponse → tek soru. Yanıt kaydı taşımıyorsa <c>null</c>.
    /// <para><b><paramref name="remoteQuestionId"/> İSTEKTEN taşınır:</b> N11 detay şeması (WSDL
    /// <c>ProductQuestionDetail</c>) <c>id</c> alanı İÇERMEZ — yanıtı hangi soruya ait olduğunu ancak isteği
    /// yapan bilir.</para>
    /// <para>Kota duvarı burada da İSTİSNA DEĞİL (liste ucuyla aynı felsefe) → <c>null</c>; ayrımı çağıranın
    /// bıraktığı log taşır.</para></summary>
    public static RemoteQuestion? ParseDetailResponse(XDocument doc, string remoteQuestionId)
    {
        if (IsAccessLimit(doc))
        {
            return null;
        }

        EnsureSuccess(doc, "TradeXpress:N11:Question:DetailRejected");

        var detail = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "productQuestion");
        if (detail is null)
        {
            return null;
        }

        return new RemoteQuestion(
            RemoteQuestionId: remoteQuestionId,
            RemoteProductId: NullIfEmpty(Local(detail, "productId")),
            ProductTitle: NullIfEmpty(Local(detail, "productTitle")),
            Subject: NullIfEmpty(Local(detail, "questionSubject")),
            QuestionText: NullIfEmpty(Local(detail, "question")),
            CustomerName: NullIfEmpty(Local(detail, "fullName")),
            CustomerEmail: NullIfEmpty(Local(detail, "email")),
            QuestionDate: ParseQuestionDate(Local(detail, "questionDate")),
            // HAM metin olduğu gibi taşınır — nötr eşleme senkron katmanında ve TOLERANT yapılır.
            RemoteStatus: NullIfEmpty(Local(detail, "status")),
            IsPublic: ParseExposeFlag(Local(detail, "buyerExpose")),
            ExistingAnswer: NullIfEmpty(Local(detail, "answer")),
            ImageUrls: ParseImageUrls(detail));
    }

    /// <summary>Liste öğelerini çevirir. <c>id</c>'siz satır ATLANIR: idempotency anahtarı (SalesChannelId +
    /// RemoteQuestionId) kurulamayan kayıt ne saklanabilir ne tazelenebilir — kaydedilmeye çalışılsa aggregate
    /// zaten reddederdi ve tüm sayfa düşerdi (emsal: <c>N11ProductClient.ParseSkus</c>).</summary>
    private static List<RemoteQuestion> ParseListItems(XDocument doc)
    {
        var container = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "productQuestions");
        if (container is null)
        {
            return new List<RemoteQuestion>();
        }

        return container.Elements()
            .Where(e => e.Name.LocalName == "productQuestion")
            .Select(ParseListItem)
            .Where(q => q.RemoteQuestionId.Length > 0)
            .ToList();
    }

    /// <summary>Tek liste öğesi (WSDL <c>ProductQuestion</c>: id/productId/productTitle/questionSubject/question/
    /// answer/images). Müşteri, tarih, durum ve görünürlük alanları listede YOKTUR → <c>null</c> bırakılır
    /// ("bilgi yok"; uygulayan katman bunlarla mevcut değeri ezmemelidir).</summary>
    private static RemoteQuestion ParseListItem(XElement item)
    {
        return new RemoteQuestion(
            RemoteQuestionId: Local(item, "id") ?? string.Empty,
            RemoteProductId: NullIfEmpty(Local(item, "productId")),
            ProductTitle: NullIfEmpty(Local(item, "productTitle")),
            Subject: NullIfEmpty(Local(item, "questionSubject")),
            QuestionText: NullIfEmpty(Local(item, "question")),
            CustomerName: null,
            CustomerEmail: null,
            QuestionDate: null,
            RemoteStatus: null,
            IsPublic: null,
            // answer LİSTEDE de var → "cevaplanmış mı" detay çağrısı YAPMADAN bilinir (kota tasarrufunun kaldıracı).
            ExistingAnswer: NullIfEmpty(Local(item, "answer")),
            ImageUrls: ParseImageUrls(item));
    }

    /// <summary>Müşterinin soruya eklediği görsellerin BAĞLANTILARI (WSDL <c>ImageList</c> → <c>image</c>, xs:string).
    /// <para>Karar (2026-08-01 Hakan): görseller DAM'a indirilmez, yalnız bağlantı saklanır.</para>
    /// <para><b>CANLI DOĞRULANMADI</b> — keşif anında hesapta soru yoktu. Şema düz metin (URL doğrudan
    /// <c>image</c> elemanının içinde) diyor; yine de alt <c>url</c> elemanlı varyanta karşı toleranslıyız,
    /// çünkü yanlış varsayım burada sessiz veri kaybı demektir.</para></summary>
    private static IReadOnlyList<string> ParseImageUrls(XElement parent)
    {
        var images = parent.Elements().FirstOrDefault(e => e.Name.LocalName == "images");
        if (images is null)
        {
            return Array.Empty<string>();
        }

        return images.Elements()
            .Where(e => e.Name.LocalName == "image")
            .Select(ReadImageUrl)
            .Where(url => url is not null)
            .Select(url => url!)
            .ToList();
    }

    private static string? ReadImageUrl(XElement image)
    {
        return NullIfEmpty(Local(image, "url")) ?? NullIfEmpty(image.Value);
    }

    /// <summary>Soru tarihi (WSDL <c>xs:date</c> — GÜN hassasiyetli, saat YOK).
    /// <para><b>Saat kaydırması UYGULANMAZ</b> — <c>N11OrderClient</c>'ın <c>createDate</c> alanının aksine
    /// (o GMT+3 timestamp'idir ve UTC'ye çekilir). Burada saat olmadığı için −3 uygulamak günü BİR GERİ kaydırırdı.
    /// Kayıt UTC işaretli midnight olarak saklanır ve zaten yalnız çapraz kontrol içindir: SLA geri sayımı
    /// <c>ChannelQuestion.FirstSeenAt</c> üzerinden akar.</para>
    /// <para>Biçim toleranslı (canlı görülmedi): N11'in her yerde kullandığı <c>dd/MM/yyyy</c> önce, ardından
    /// xs:date'in kanonik <c>yyyy-MM-dd</c> biçimi ve saatli varyantlar.</para></summary>
    private static DateTime? ParseQuestionDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[] { "dd/MM/yyyy", "yyyy-MM-dd", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm" };
        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
        }

        return null;
    }

    /// <summary>N11'in <c>sellerExpose</c>/<c>buyerExpose</c> alanları BELGELENMEMİŞTİR (serbest metin, canlı
    /// görülmedi). Yalnız TARTIŞMASIZ boolean değerler kabul edilir; tanınmayan her şey <c>null</c> =
    /// "bilinmiyor" olur. Gerekçe: doğrulanmamış bir değerden "herkese açık" etiketi üretmek müşteri
    /// mahremiyeti açısından risklidir (bkz. <c>ChannelQuestion.IsPublic</c>).</summary>
    private static bool? ParseExposeFlag(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1")
        {
            return true;
        }

        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) || text == "0")
        {
            return false;
        }

        return null;
    }

    // ── Sonuç bloğu (ResultInfo) ─────────────────────────────────────────────────────────────────────

    /// <summary>Kota duvarı mı? İşaret hem <c>errorCode</c> hem <c>errorMessage</c> içinde aranır (N11 hata
    /// kodunu uç adıyla ön-ekler: <c>SELLER_API.getProductQuestionListRequest.accessLimit.reached</c>).</summary>
    private static bool IsAccessLimit(XDocument doc)
    {
        var result = FindResult(doc);
        if (result is null || IsSuccess(result))
        {
            return false;
        }

        return MarksAccessLimit(Local(result, "errorCode")) || MarksAccessLimit(Local(result, "errorMessage"));

        static bool MarksAccessLimit(string? value)
        {
            return value is not null && value.Contains(AccessLimitMarker, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary><c>result/status != success</c> → hata mesajını taşıyan dostane <c>BusinessException</c>.
    /// <c>result</c> bloğu HİÇ yoksa da hata sayılır (fail-closed): N11 bu serviste her yanıta ekler, yokluğu
    /// beklenmedik bir body demektir ve sessizce "boş liste" saymak veri kaybını gizlerdi.</summary>
    private static void EnsureSuccess(XDocument doc, string errorCode)
    {
        var result = FindResult(doc);
        if (result is not null && IsSuccess(result))
        {
            return;
        }

        var message = result is null ? null : NullIfEmpty(Local(result, "errorMessage"));
        var status = result is null ? null : NullIfEmpty(Local(result, "status"));
        throw new BusinessException(errorCode).WithData("message", message ?? status ?? "unknown");
    }

    /// <summary><c>status</c> aranırken KÖKTEN değil <c>result</c> bloğundan okunur: soru DETAYI da <c>status</c>
    /// adında bir alan taşır (sorunun kendi durumu) — kör <c>Descendants</c> araması yanlış elemanı yakalayabilir.</summary>
    private static XElement? FindResult(XDocument doc)
    {
        return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "result");
    }

    private static bool IsSuccess(XElement result)
    {
        return string.Equals(Local(result, "status"), "success", StringComparison.OrdinalIgnoreCase);
    }

    // ── HTTP + yardımcılar ───────────────────────────────────────────────────────────────────────────

    /// <summary>Kanalın N11 kimliğini çözer. Kanal bulunamazsa (yanlış id ya da tenant/şirket kapsamı dışında)
    /// dostane hata — çağıranın doğru kapsamda (tenant + şirket) çalışıyor olması gerekir.</summary>
    private async Task<(string AppKey, string AppSecret)> ResolveCredentialsAsync(
        Guid salesChannelId, CancellationToken cancellationToken)
    {
        var channel = await _channelRepository.FindAsync(salesChannelId, cancellationToken: cancellationToken)
            ?? throw new BusinessException("TradeXpress:N11:Question:ChannelNotFound")
                .WithData("channelId", salesChannelId);

        return (channel.AppKey, channel.AppSecret);
    }

    private async Task<XDocument> PostAsync(
        XElement request, string appKey, string appSecret, string transportErrorCode, CancellationToken cancellationToken)
    {
        var envelope = new XDocument(new XElement(Soapenv + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soapenv", Soapenv),
            new XElement(Soapenv + "Header"),
            new XElement(Soapenv + "Body", request)));

        using var content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        content.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
        httpRequest.Headers.TryAddWithoutValidation("appkey", appKey);
        httpRequest.Headers.TryAddWithoutValidation("appsecret", appSecret);

        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(transportErrorCode).WithData("status", (int)response.StatusCode);
        }

        return XDocument.Parse(body);
    }

    private static string? Local(XElement? parent, string localName)
    {
        return parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
