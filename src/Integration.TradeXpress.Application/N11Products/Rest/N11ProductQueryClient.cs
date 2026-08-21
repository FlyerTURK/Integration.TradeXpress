using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 REST ürün <b>LİSTELEME</b> istemcisi — <c>GET /ms/product-query</c> (resmî doküman v9.0 "3.6").
/// <b>SALT OKUMA / SENKRON:</b> yazma uçlarının aksine taskId yoktur, yanıt anında gelir.
/// Kimlik: <c>appkey</c>/<c>appsecret</c> HTTP başlığı (<see cref="N11RestClientBase"/>).
/// </summary>
public interface IN11ProductQueryClient : ITransientDependency
{
    /// <summary>Tek sayfa sorgular. <c>filter.Size</c> 50'yi aşarsa <b>sessizce 50'ye kırpılır</b> (doküman sınırı).
    /// Ağ/HTTP hatasında taban sınıf BusinessException fırlatır; yanıt alanları eksikse PATLAMAZ (null bırakır).</summary>
    Task<N11RestProductPage> QueryAsync(
        N11ProductQueryFilter filter, string appKey, string appSecret, CancellationToken cancellationToken = default);

    /// <summary>Filtrenin TÜM sayfalarını gezip birleştirir (<c>filter.Page</c> başlangıç sayfasıdır).
    /// Durma ölçütü: boş <c>content</c> (dokümanın önerdiği son-sayfa işareti) <b>veya</b> <c>totalPages</c> tükenmesi.
    /// Güvenlik tavanı 500 sayfa. ⚠ Sayfalar arası N11 tarafında ürün eklenip çıkarsa aynı SKU iki kez düşebilir —
    /// çağıran <b>idempotent upsert</b> yapmalıdır (istemci tekilleştirme YAPMAZ, sırayı bozmamak için).</summary>
    Task<IReadOnlyList<N11RestProductSummary>> QueryAllAsync(
        N11ProductQueryFilter filter, string appKey, string appSecret, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IN11ProductQueryClient"/> uygulaması. Ayrıştırma <b>savunmacı</b>dır: her alan
/// <see cref="JsonElement.TryGetProperty"/> ile okunur, tip/alan eksikse <c>null</c> bırakılır — N11 yanıt şeklini
/// genişlettiğinde (doküman: alan genişlikleri/tipleri artabilir) import akışı çökmez.
/// Yanıt <b>görsel TAŞIR</b> (<c>imageUrls</c>); ayrıntı <see cref="N11RestProductSummary"/> doc'unda.
/// </summary>
public sealed class N11ProductQueryClient : N11RestClientBase, IN11ProductQueryClient
{
    /// <summary>Resmî REST dokümanı (2026-02-04, satır 1010): "size Varsayılan 20 maksimum <b>250</b>".
    /// Aşan istek KIRPILIR — N11'in sessizce kendi varsayılanına düşürmesi sayfalamayı kaydırır, o yüzden
    /// sınırı biz uygularız.
    ///
    /// <para>Eskiden 50 idi: o değer v9.0 SOAP dokümanından (satır 1683) geliyordu ve REST için BAYATTI —
    /// 5000 ürünlük bir mağaza içe aktarımı 100 istek yerine 20 istekle biter (2026-08-03 düzeltmesi).</para></summary>
    private const int MaxPageSize = 250;

    /// <summary>Doküman varsayılanı — <c>Size</c> geçersiz (≤0) verilirse buna düşülür.</summary>
    private const int DefaultPageSize = 20;

    /// <summary>Sayfa döngüsü güvenlik tavanı (emsal: <c>OrderSyncManager</c> MaxPageLoops) — bozuk/çelişkili
    /// <c>totalPages</c> sonsuz döngüye çevirmesin.</summary>
    private const int MaxPageLoops = 500;

    private readonly ILogger<N11ProductQueryClient> _logger;

    public N11ProductQueryClient(ILogger<N11ProductQueryClient> logger, IOptions<N11EndpointOptions> endpointOptions)
        : base(endpointOptions)
    {
        _logger = logger;
    }

    // ── Tek sayfa ───────────────────────────────────────────────────────────────────────────────────

    public async Task<N11RestProductPage> QueryAsync(
        N11ProductQueryFilter filter, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        Check.NotNull(filter, nameof(filter));

        var url = BuildUrl(filter, RestQueryBase);
        var body = await RestSendAsync(HttpMethod.Get, url, null, appKey, appSecret, cancellationToken);
        return ParsePage(body, url, filter);
    }

    // ── Tüm sayfalar ────────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<N11RestProductSummary>> QueryAllAsync(
        N11ProductQueryFilter filter, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        Check.NotNull(filter, nameof(filter));

        var all = new List<N11RestProductSummary>();
        var page = filter.Page < 0 ? 0 : filter.Page;
        var totalPages = 0;
        var loops = 0;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = await QueryAsync(filter with { Page = page }, appKey, appSecret, cancellationToken);
            if (current.Items.Count == 0)
            {
                break;   // doküman: "content boş dönen sayfayı son sayfa olarak belirleyebilirsiniz"
            }

            all.AddRange(current.Items);
            totalPages = current.TotalPages;
            page++;
            loops++;
        }
        while (page < totalPages && loops < MaxPageLoops);

        // Yalnız GERÇEKTEN tavan yüzünden kesildiyse uyar (sayfa sayısı tam 500 olup doğal bittiyse uyarma).
        if (loops >= MaxPageLoops && page < totalPages)
        {
            _logger.LogWarning(
                "N11 product-query sayfa tavanına ({MaxPageLoops}) takıldı — totalPages={TotalPages}. Liste EKSİK.",
                MaxPageLoops, totalPages);
        }

        return all;
    }

    // ── URL kurulumu ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sorgu URL'ini kurar. <b><c>internal</c> (private değil):</b> "size 250'ye kırpılır / geçersiz sayfa 0'a düşer"
    /// kuralı dokümanın belgelenmiş sınırıdır ve tek gözlenebilir yeri bu URL'dir — sözleşme testi (
    /// <c>N11RestContractTests</c>, InternalsVisibleTo) ağa çıkmadan yalnız buradan doğrulayabilir.
    ///
    /// <para><b>STATİK kalması bilinçli:</b> taban adres artık yapılandırılabilir olduğu için parametreye taşındı.
    /// Metodu örnek hâline getirmek, sözleşme testinin URL kuralını sınamak için <c>IOptions</c> kurup istemci
    /// örneği yaratmasını gerektirirdi — saf kuralı sınayan test bağımlılık kurmamalı.</para>
    /// </summary>
    internal static string BuildUrl(N11ProductQueryFilter filter, string queryBase)
    {
        var page = filter.Page < 0 ? 0 : filter.Page;

        // Üst sınır AŞILIRSA kırpılır: fazlası sessizce reddedilip 20'ye düşerse çağıranın sayfa hesabı
        // kayar (aynı kayıtları tekrar okur) — sınırı burada zorluyoruz.
        var size = filter.Size <= 0 ? DefaultPageSize : Math.Min(filter.Size, MaxPageSize);

        var sb = new StringBuilder(queryBase);
        sb.Append("?page=").Append(page.ToString(CultureInfo.InvariantCulture));
        sb.Append("&size=").Append(size.ToString(CultureInfo.InvariantCulture));

        // Boş filtre alanı sorgu dizesine HİÇ yazılmaz (doküman: parametreler opsiyonel).
        AppendIfPresent(sb, "stockCode", filter.StockCode);
        AppendIfPresent(sb, "saleStatus", filter.SaleStatus);
        AppendIfPresent(sb, "productStatus", filter.ProductStatus);
        AppendIfPresent(sb, "brandName", filter.BrandName);
        AppendIfPresent(sb, "categoryIds", filter.CategoryIds);

        return sb.ToString();
    }

    private static void AppendIfPresent(StringBuilder sb, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        sb.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value.Trim()));
    }

    // ── Yanıt ayrıştırma (savunmacı) ────────────────────────────────────────────────────────────────

    private N11RestProductPage ParsePage(string body, string url, N11ProductQueryFilter filter)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            // Body JSON değil (WAF/HTML hata sayfası vb.) — HTTP 200 gelmiş olsa bile kullanılabilir veri YOK.
            // Kök neden loglanır, dışarı taban sınıfın hata koduyla çıkılır (yeni lokalizasyon anahtarı açmadan).
            // URL yalnız filtre parametreleri taşır — appkey/appsecret BAŞLIKTADIR, loga sızmaz.
            _logger.LogError(ex, "N11 product-query yanıtı JSON değil: {Url}", url);
            throw new BusinessException("TradeXpress:N11:Rest:RequestFailed", innerException: ex)
                .WithData("Url", url)
                .WithData("Reason", "InvalidJson");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var items = new List<N11RestProductSummary>();

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in content.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        items.Add(ReadSummary(element));
                    }
                }
            }

            // number = 0-tabanlı mevcut sayfa; yoksa istediğimiz sayfayı varsay.
            var page = ReadInt(root, "number") ?? (filter.Page < 0 ? 0 : filter.Page);
            var totalPages = ReadInt(root, "totalPages") ?? 0;
            var totalCount = ReadLong(root, "totalElements") ?? items.Count;

            return new N11RestProductPage(items, page, totalPages, totalCount);
        }
    }

    private static N11RestProductSummary ReadSummary(JsonElement element)
    {
        return new N11RestProductSummary(
            ReadLong(element, "n11ProductId") ?? 0L,       // long: doküman 9→10 hane genişleme uyarısı
            ReadString(element, "productMainId"),
            ReadString(element, "stockCode") ?? string.Empty,
            ReadString(element, "title"),
            ReadDecimal(element, "salePrice"),
            ReadDecimal(element, "listPrice"),
            ReadInt(element, "quantity"),
            ReadString(element, "saleStatus"),
            // Yanıtta ürün aktifliği "status" adıyla gelir (istek parametresi "productStatus" — ADLAR FARKLI).
            // N11 ileride yanıtı istek adıyla hizalarsa diye ikinci ad da denenir.
            ReadString(element, "status") ?? ReadString(element, "productStatus"),
            ReadString(element, "categoryId"),             // yanıtta SAYI gelir → string'e düşürülür
            ReadStringArray(element, "imageUrls"));        // mağaza içe aktarımında DAM'a beslenecek URL listesi
    }

    /// <summary>Görsel URL dizisi — yanıtta yoksa BOŞ liste (null değil): çağıran "görsel yok" ile
    /// "alan gelmedi" ayrımını yapmak zorunda kalmasın. Dizi olmayan/boş girdiler elenir.</summary>
    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } url)
            {
                list.Add(url);
            }
        }

        return list;
    }

    // ── Tip-toleranslı okuyucular ───────────────────────────────────────────────────────────────────
    // N11 aynı alanı bazen sayı bazen string verebiliyor (doküman: tip/genişlik değişebilir) → her okuyucu
    // iki gösterimi de kabul eder, çözemezse null döner (asla exception).

    private static string? ReadString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => NullIfBlank(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),   // kültürden bağımsız ham yazım
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,                                    // null / dizi / nesne → bilgi yok
        };
    }

    private static long? ReadLong(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt64(out var number))
            {
                return number;
            }

            // "123456789.0" gibi ondalık yazım da tamsayı kimliktir
            if (value.TryGetDecimal(out var asDecimal) && asDecimal >= long.MinValue && asDecimal <= long.MaxValue)
            {
                return decimal.ToInt64(decimal.Truncate(asDecimal));
            }

            return null;
        }

        if (value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? ReadInt(JsonElement obj, string propertyName)
    {
        var value = ReadLong(obj, propertyName);
        if (value is null || value.Value < int.MinValue || value.Value > int.MaxValue)
        {
            return null;
        }

        return (int)value.Value;
    }

    private static decimal? ReadDecimal(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        // String gelirse N11 fiyatı NOKTA ondalıklı yazar → InvariantCulture zorunlu (virgül yorumu yanlış olur).
        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
