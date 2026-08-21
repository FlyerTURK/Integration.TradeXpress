using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 REST ailesinin ORTAK tabanı (v9.0 dokümanı, <c>/ms/product…</c> uçları). Bu sınıfta toplanan üç şey:
/// (1) kimlik başlıkları, (2) tek <see cref="System.Net.Http.HttpClient"/> örneği, (3) ortak JSON sözleşmesi.
/// <para>
/// <b>Kimlik:</b> REST tarafında <c>Authorization</c> KULLANILMAZ; kimlik doğrudan <c>appkey</c> + <c>appsecret</c>
/// HTTP başlıklarıyla taşınır (SOAP'ta body'deki <c>&lt;auth&gt;</c> bloğunun karşılığı). Aynı AppKey/AppSecret çifti,
/// aynı başlık adları — <see cref="N11Categories.N11CategoryClient"/> bu deseni bugün canlıda çalıştırıyor.
/// <b>Sır ASLA loglanmaz ve exception verisine KONMAZ</b> — hata yalnız URL + HTTP statü taşır.
/// </para>
/// <para>
/// <b>Bu taban SOAP yolunun YERİNE GEÇMEZ.</b> Mevcut <see cref="N11ProductClient"/> (SOAP) olduğu gibi durur;
/// REST yolu onun YANINA eklenir, kullanım yerinin seçimi ayrı bir karardır.
/// </para>
/// </summary>
public abstract class N11RestClientBase
{
    /// <summary>Uç adresleri — TEK kaynak (<see cref="N11EndpointOptions"/>). Eskiden burada iki <c>const</c>
    /// vardı; adres yapılandırılabilir olmadan istekleri yerel bir sahte sunucuya yönlendirmek mümkün değildi
    /// (hesap erişimi kapalıyken N11 kodunu denemenin tek yolu bu). Varsayılan bugünkü adres.</summary>
    protected N11EndpointOptions Endpoints { get; }

    /// <summary>Ürün YAZMA + task sorgulama uçlarının tabanı: <c>/tasks/product-create</c>,
    /// <c>/tasks/product-update</c>, <c>/tasks/price-stock-update</c>, <c>/task-details/page-query</c>.</summary>
    protected string RestProductBase
    {
        get { return Endpoints.RestProductBase; }
    }

    /// <summary>Satıcı ürünlerini listeleme ucu — tek SENKRON REST ucu (yazma uçları asenkrondur).</summary>
    protected string RestQueryBase
    {
        get { return Endpoints.RestQueryBase; }
    }

    protected N11RestClientBase(IOptions<N11EndpointOptions> endpointOptions)
    {
        Check.NotNull(endpointOptions, nameof(endpointOptions));
        Endpoints = endpointOptions.Value;
    }

    /// <summary>
    /// Tüm N11 REST istemcileri için TEK <see cref="System.Net.Http.HttpClient"/> (socket tükenmesini önler).
    /// <c>PooledConnectionLifetime</c>, statik istemcinin klasik DNS-bayatlaması sorununa karşı bağlantıları
    /// periyodik tazeler. Timeout 100 sn (asenkron uçlar kuyruğa alıp hemen döndüğü için cömert olması gerekmez,
    /// ama 1000 SKU'luk body'lerin yüklenmesi zaman alabilir).
    /// </summary>
    private static readonly HttpClient HttpClient = new(
        new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = TimeSpan.FromSeconds(100),
    };

    /// <summary>
    /// N11 REST'in ORTAK JSON sözleşmesi. <b>Türetilen istemciler body'yi bununla üretmek ZORUNDADIR</b>
    /// (<see cref="SerializeJson{T}"/>).
    /// <list type="bullet">
    /// <item><c>CamelCase</c> — N11 alan adları (<c>stockCode</c>, <c>listPrice</c>, <c>salePrice</c>) camelCase'dir.</item>
    /// <item><c>WhenWritingNull</c> — dokümanın <i>"istekte mevcut olmayan alanlar için herhangi bir update
    /// yapılmayacaktır"</i> kuralının <b>MEKANİK GARANTİSİ</b>: <c>null</c> bırakılan alan JSON'a hiç yazılmaz,
    /// dolayısıyla N11'de de değişmez. Model tarafında "güncelleme" ile "boşalt" ayrımı <c>null</c> ile yapılır —
    /// bu yüzden opsiyonel alanlar <c>decimal?</c>/<c>int?</c> gibi NULLABLE tanımlanmalıdır (değer tipinin
    /// varsayılanı <c>0</c> atlanmaz, gönderilir ve N11'de fiyatı/stoğu sıfırlar).</item>
    /// <item>Sayı biçimlendirmesi: <c>System.Text.Json</c> ondalıkları HER ZAMAN invariant kültürle (NOKTA ile)
    /// yazar ⇒ dokümanın "virgül hata verir" kuralı yapısal olarak sağlanır. <b>"Noktadan sonra tam 2 hane"</b>
    /// kuralı ise serializer'ın işi DEĞİLDİR; fiyat alanlarını gönderen istemci yuvarlamayı kendisi yapmalıdır.</item>
    /// </list>
    /// </summary>
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,   // yanıt okurken N11'in alan adı kasasına bağımlı kalmayalım
    };

    /// <summary>Body'yi ORTAK sözleşmeyle (<see cref="JsonOptions"/>) serialize eder — null-atlama kuralı burada garanti altına alınır.</summary>
    protected static string SerializeJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// N11 REST'e istek gönderir ve ham yanıt body'sini döndürür.
    /// <para>
    /// <b>DİKKAT — HTTP 200 "işlem başarılı" DEMEK DEĞİLDİR.</b> Yazma uçları (<c>product-create</c>,
    /// <c>product-update</c>, <c>price-stock-update</c>) ASENKRONDUR: 200 yalnız "istek kuyruğa alındı" der ve
    /// <c>taskId</c> döner. Gerçek SKU-bazlı sonuç <see cref="N11TaskPoller"/> ile sorgulanır.
    /// Bu metot yalnız TAŞIMA katmanı başarısını (2xx) doğrular.
    /// </para>
    /// </summary>
    /// <param name="jsonBody">GET uçlarında <c>null</c> geçilir; doluysa <c>Content-Type: application/json</c> eklenir.</param>
    protected static async Task<string> RestSendAsync(
        HttpMethod method,
        string url,
        string? jsonBody,
        string appKey,
        string appSecret,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("appkey", appKey);
        request.Headers.TryAddWithoutValidation("appsecret", appSecret);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (jsonBody is not null)
        {
            // Content-Type ELLE kurulur: StringContent'in ürettiği "; charset=utf-8" ekini istemiyoruz
            // (bazı Java yığınları medya tipini birebir eşler). Body yine UTF-8 baytlarla gider.
            var content = new StringContent(jsonBody, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Content = content;
        }

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // KİMLİK reddi AYRI mesaj alır (2026-08-06): düz "istek başarısız (401)" görünce ilk refleks kodda
            // hata aramak oluyor — oysa 401'in tek anlamı N11'in kimliği kabul etmemesidir (anahtar yanlış ya
            // da satıcı hesabı pasif). Yanlış yerde arama maliyeti bir kez ödendi, mesaj o yüzden ayrıldı.
            var errorCode = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "TradeXpress:N11:Rest:Unauthorized"
                : "TradeXpress:N11:Rest:RequestFailed";

            // Sır (appkey/appsecret) veri olarak EKLENMEZ — yalnız uç + statü + kırpılmış yanıt.
            throw new BusinessException(errorCode)
                .WithData("Url", url)
                .WithData("Status", (int)response.StatusCode)
                .WithData("Response", Truncate(body, 2000));
        }

        return body;
    }

    /// <summary>Hata verisine konan yanıt body'sini sınırlar — N11 bazen çok uzun HTML hata sayfası döner.</summary>
    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
