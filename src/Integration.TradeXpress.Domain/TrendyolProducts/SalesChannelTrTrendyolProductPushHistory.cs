using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannelProducts;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol'a gönderilen bir SKU'nun tarihli kaydı — <b>APPEND-ONLY</b> (N11 Faz-4 simetriği).
///
/// <para><b>Neden var:</b> <c>SalesChannelTrTrendyolProductSku.LastSent*</c> her gönderimde ÜZERİNE yazılıyor —
/// yani "şu tarihte şu fiyata gönderdim" cümlesini kuramıyorduk. Pazaryeri kendi tarafında versiyon saklarken
/// bizim elimizde yalnız SON hâl oluyordu. Bu tablo o boşluğu kapatır.</para>
///
/// <para><b>N11'DEN TEK FARKI — YAZIM ANI:</b> N11'de kayıt <b>submit başarısında</b> yazılır (REST yazımı
/// senkron çözülebiliyor). Trendyol'da yazım <b>batch COMPLETED olduğunda</b> yapılır, submit anında DEĞİL —
/// çünkü Trendyol yazma uçları asenkron ve <b>batch reddedilebilir</b>. Submit anında yazsaydık delil
/// "gönderdim" derdi; oysa delilin söylemesi gereken şey "kabul edildi"dir. Reddedilen bir batch'in geçmişte
/// başarılı görünmesi, delil kaydını delil olmaktan çıkarırdı.</para>
///
/// <para><b>BAŞARISIZ GÖNDERİM DE YAZILIR</b> (2026-08-10 Hakan kararı) — <see cref="Outcome"/> +
/// <see cref="ErrorMessage"/> ile. Eski kural "reddedileni hiç yazma"ydı; amacı reddedileni <b>başarılı
/// sanmayı</b> önlemekti ve o amaç korunuyor: satır artık susmak yerine <c>Failed</c> diyor. Susmanın bedeli
/// şuydu — bir fiyatın kanala yansımamış olmasının sebebi hiçbir yerde kalmıyor, "denendi ve reddedildi" ile
/// "hiç denenmedi" ayırt edilemiyordu. <c>LastSent*</c> terfisi DEĞİŞMEDİ: yalnız kabul edilen gönderim
/// kıyas tabanını ilerletir.</para>
///
/// <para><b>APPEND-ONLY sözleşmesi:</b> bu kayıtlar GÜNCELLENMEZ ve SİLİNMEZ. Değiştirilebilir bir delil
/// delil değildir → entity'de hiçbir <c>Set*</c> metodu YOKTUR; tüm alanlar ctor + tek <c>Fill</c> ile yazılır.
/// Sonuç bu yüzden ctor'da ZORUNLUDUR: unutulan bir bayrak, başarısızı başarılı gösterirdi.</para>
///
/// <para><b>Geriye dönük çalışmaz:</b> ilk kayıt bu özellik devreye girdikten SONRAKİ ilk COMPLETED batch'ten
/// başlar. Daha önce gönderilenlerin geçmişi yoktur ve üretilemez — bu yüzden ilk gerçek push'tan ÖNCE kurulur.</para>
/// </summary>
public class SalesChannelTrTrendyolProductPushHistory : CreationAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrTrendyolProductPushHistory()
    {
    }

    public SalesChannelTrTrendyolProductPushHistory(
        Guid companyId,
        Guid salesChannelTrTrendyolProductId,
        string barcode,
        DateTime pushedAtUtc,
        TrendyolProductPushKind pushKind,
        ChannelPushOutcome outcome)
    {
        CompanyId = companyId;
        SalesChannelTrTrendyolProductId = salesChannelTrTrendyolProductId;
        Barcode = barcode;
        PushedAtUtc = pushedAtUtc;
        PushKind = pushKind;
        Outcome = outcome;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Hangi kanal ürününün geçmişi — id-only (FK yok, aggregate'ler arası referans kuralı).</summary>
    public virtual Guid SalesChannelTrTrendyolProductId { get; protected set; }

    /// <summary>Trendyol'un SKU kimliği. <b>Stok kodu DEĞİL barkod</b> — fiyat/stok ucu barkodla adresliyor
    /// ve ikisi Trendyol'da farklı olabiliyor; delilde yanlış olanı saklamak eşleştirmeyi imkânsızlaştırırdı.</summary>
    public virtual string Barcode { get; protected set; } = string.Empty;

    /// <summary>Gönderim anı (UTC). Kayıt=UTC / görüntü=kullanıcı yereli kuralı (CLAUDE.md §6).</summary>
    public virtual DateTime PushedAtUtc { get; protected set; }

    /// <summary>Gönderimin türü — delil değerini belirler.</summary>
    public virtual TrendyolProductPushKind PushKind { get; protected set; }

    /// <summary>Gönderilen liste fiyatı (üstü çizili). Trendyol'da indirim ayrı bir ALAN değil,
    /// liste/satış farkıdır → ikisi birlikte saklanmazsa indirim delili kurulamaz.</summary>
    public virtual decimal? ListPrice { get; protected set; }

    /// <summary>Gönderilen satış fiyatı — müşterinin ödediği sayı.</summary>
    public virtual decimal? SalePrice { get; protected set; }

    /// <summary>Gönderilen adet.</summary>
    public virtual int? Quantity { get; protected set; }

    /// <summary>Gönderilen başlık — içerik versiyonunun parçası (yalnız tam push'ta anlamlı).</summary>
    public virtual string? Title { get; protected set; }

    /// <summary>Gönderilen varyant seçenekleri, "ad=değer" biçiminde birleştirilmiş.
    /// <i>Ayrı tabloya bölünmedi: delil kaydı OKUNUR, sorgulanmaz.</i></summary>
    public virtual string? VariantOptions { get; protected set; }

    /// <summary>Gönderilen görsellerin DAM kimlikleri + içerik hash'leri ("id:hash", sıralı).
    /// <c>MediaId</c> "hangi kayıt" der, <c>ContentHash</c> içeriğin sonradan değişmediğini kanıtlar.</summary>
    public virtual string? Images { get; protected set; }

    /// <summary>Bu gönderimi taşıyan Trendyol batch kimliği — karşı tarafla eşleştirme anahtarı.</summary>
    public virtual string? BatchRequestId { get; protected set; }

    /// <summary>Denemenin SONUCU — kabul edildi mi reddedildi mi. Zorunlu (ctor'da yazılır).
    /// Bkz. <see cref="ChannelPushOutcome"/>.</summary>
    public virtual ChannelPushOutcome Outcome { get; protected set; }

    /// <summary>Reddin gerekçesi — Trendyol'un batch sonucunda döndürdüğü mesaj (kısmi başarıda kaç kalemin
    /// düştüğü de burada). <see cref="ChannelPushOutcome.Succeeded"/> satırlarda <c>null</c>.</summary>
    public virtual string? ErrorMessage { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Ne gönderildiğini yazar. <b>Yalnız ctor'dan sonra BİR KEZ</b> çağrılır; append-only sözleşmesi
    /// gereği sonradan değiştirilmez — bu yüzden ayrı <c>Set*</c> metotları yoktur.</summary>
    public virtual void Fill(
        decimal? listPrice,
        decimal? salePrice,
        int? quantity,
        string? title,
        IEnumerable<(string Name, string Value)>? options,
        IEnumerable<(Guid MediaId, string? ContentHash)>? images,
        string? batchRequestId,
        string? errorMessage = null)
    {
        ErrorMessage = errorMessage;
        ListPrice = listPrice;
        SalePrice = salePrice;
        Quantity = quantity;
        Title = title;
        VariantOptions = options is null
            ? null
            : string.Join("; ", options.Select(o => o.Name + "=" + o.Value));
        Images = images is null
            ? null
            : string.Join(",", images.Select(i => i.MediaId.ToString("N") + ":" + (i.ContentHash ?? string.Empty)));
        BatchRequestId = batchRequestId;
    }

    public override string ToString()
    {
        return Barcode + "@" + PushedAtUtc.ToString("O");
    }

    #endregion
}

/// <summary>Gönderimin türü — delil değerini belirler.</summary>
public enum TrendyolProductPushKind : byte
{
    /// <summary>Tam ürün oluşturma (başlık/görsel/nitelik dâhil).</summary>
    Create = 0,

    /// <summary>Yalnız fiyat/stok senkronu — içerik değişmez.</summary>
    PriceStockSync = 1,

    /// <summary>Yalnız içerik güncelleme (başlık/açıklama/görsel/nitelik); fiyat ve stok DEĞİŞMEZ.</summary>
    ContentUpdate = 2,
}
