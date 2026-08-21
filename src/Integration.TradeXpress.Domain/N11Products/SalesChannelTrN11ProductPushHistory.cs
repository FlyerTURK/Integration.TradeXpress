using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannelProducts;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11'e GÖNDERİLEN bir SKU'nun tarihli kaydı — <b>APPEND-ONLY</b> (2026-08-05 Hakan kararı).
///
/// <para><b>Neden var:</b> N11 ürünün her versiyonunu <b>görseliyle birlikte DELİL olarak</b> saklıyor
/// ("23/07/2026'da şu varyantı şu fiyata satmıştın") ve aynı üründe görsel değiştiği için iki farklı
/// siparişte farklı resim göründüğü kullanıcı tarafından YAŞANDI. Bizde ise
/// <c>SalesChannelTrN11ProductSku.LastSent*</c> her push'ta ÜZERİNE yazılıyor — yani karşı taraf tarihli
/// kayıt gösterirken biz aynı cümleyi kuramıyorduk. Bu tablo o boşluğu kapatır.</para>
///
/// <para><b>BAŞARISIZ GÖNDERİM DE YAZILIR</b> (2026-08-10 Hakan kararı) — <see cref="Outcome"/> +
/// <see cref="ErrorMessage"/> ile. PushHistory yalnız başarıyı yazdığı sürece "denendi ve reddedildi" ile "hiç
/// denenmedi" ayırt edilemiyordu; otonom fiyat/stok güncellemesinde bir fiyatın kanala yansımama sebebini
/// ancak deneme kaydı söyleyebilir. <c>LastSent*</c> terfisi DEĞİŞMEDİ — kıyas tabanını yalnız başarı ilerletir.</para>
///
/// <para><b>APPEND-ONLY sözleşmesi:</b> bu kayıtlar GÜNCELLENMEZ ve SİLİNMEZ. Değiştirilebilir bir delil
/// delil değildir. Bu yüzden entity'de hiçbir <c>Set*</c> metodu YOKTUR — tüm alanlar ctor'da yazılır ve
/// <c>protected set</c> ile kapalıdır. Sonuç bayrağı ctor'da ZORUNLUDUR: unutulsaydı başarısız gönderim
/// başarılı görünürdü — tam da bu PushHistory'nin önlemek için var olduğu hata.</para>
///
/// <para><b>Görsel neden <c>MediaId</c> + <c>ContentHash</c> birlikte:</b> <c>MediaId</c> tek başına
/// "hangi kayıt" der ama içeriğin sonradan değişmediğini KANITLAMAZ; <c>ContentHash</c> ise içeriği
/// sabitleyip sonradan değişmişse bunu tespit edilebilir kılar. (Bugünkü DAM'da içerik blob'u zaten
/// üzerine yazılmıyor — hash o güvenceyi belgeler, varsayım olmaktan çıkarır.)</para>
///
/// <para><b>Geriye dönük çalışmaz:</b> ilk kayıt bu özellik devreye girdikten SONRAKİ ilk push'tan başlar.
/// Daha önce gönderilenlerin geçmişi yoktur ve üretilemez.</para>
/// </summary>
public class SalesChannelTrN11ProductPushHistory : CreationAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrN11ProductPushHistory()
    {
    }

    public SalesChannelTrN11ProductPushHistory(
        Guid companyId,
        Guid salesChannelTrN11ProductId,
        string sellerStockCode,
        DateTime pushedAtUtc,
        N11ProductPushKind pushKind,
        ChannelPushOutcome outcome)
    {
        CompanyId = companyId;
        SalesChannelTrN11ProductId = salesChannelTrN11ProductId;
        SellerStockCode = sellerStockCode;
        PushedAtUtc = pushedAtUtc;
        PushKind = pushKind;
        Outcome = outcome;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Hangi kanal ürününün geçmişi — id-only (FK yok, aggregate'ler arası referans kuralı).</summary>
    public virtual Guid SalesChannelTrN11ProductId { get; protected set; }

    /// <summary>BİZİM stok kodumuz — REST bu kodla adresliyor, sipariş eşleşmesi de bununla kuruluyor.</summary>
    public virtual string SellerStockCode { get; protected set; } = string.Empty;

    /// <summary>Gönderim anı (UTC). Kayıt=UTC / görüntü=kullanıcı yereli kuralı (CLAUDE.md §6).</summary>
    public virtual DateTime PushedAtUtc { get; protected set; }

    /// <summary>Tam ürün push'u mu, yalnız fiyat/stok senkronu mu — ikisinin delil değeri farklıdır
    /// (tam push başlık/görsel de gönderir, senkron yalnız adet/fiyat).</summary>
    public virtual N11ProductPushKind PushKind { get; protected set; }

    /// <summary>Gönderilen satış fiyatı.</summary>
    public virtual decimal? SalePrice { get; protected set; }

    /// <summary>Fiyatın para birimi — "150" tek başına delil değildir.</summary>
    public virtual string? CurrencyType { get; protected set; }

    /// <summary>Gönderilen adet.</summary>
    public virtual int? Quantity { get; protected set; }

    /// <summary>Gönderilen başlık — N11 içerik versiyonunun bir parçası.</summary>
    public virtual string? Title { get; protected set; }

    /// <summary>Gönderilen varyant seçenekleri, "ad=değer" biçiminde birleştirilmiş.
    /// <i>Ayrı tabloya bölünmedi: PushHistory kaydı OKUNUR, sorgulanmaz — normalizasyon burada karmaşıklık katardı.</i></summary>
    public virtual string? VariantOptions { get; protected set; }

    /// <summary>Gönderilen görsellerin DAM kimlikleri + içerik hash'leri (sıralı, "id:hash" biçiminde).
    /// Sıra ÖNEMLİ — N11'de ilk görsel vitrindir.</summary>
    public virtual string? Images { get; protected set; }

    /// <summary>N11'in bu gönderim için döndürdüğü kimlik (task/ürün) — karşı tarafla eşleştirme anahtarı.</summary>
    public virtual string? RemoteReference { get; protected set; }

    /// <summary>Denemenin SONUCU — ulaştı mı ulaşmadı mı. Zorunlu (ctor'da yazılır); gerekçesi
    /// <see cref="ErrorMessage"/>'dedir. Bkz. <see cref="ChannelPushOutcome"/>.</summary>
    public virtual ChannelPushOutcome Outcome { get; protected set; }

    /// <summary>Başarısızlığın gerekçesi — kanalın döndürdüğü mesaj ya da guard'ın açıklaması.
    /// <see cref="ChannelPushOutcome.Succeeded"/> satırlarda <c>null</c>: başarıda anlatılacak bir sebep
    /// yoktur ve boş bir metin "sebep bilinmiyor" gibi okunurdu.</summary>
    public virtual string? ErrorMessage { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Ne gönderildiğini yazar. <b>Yalnız ctor'dan sonra BİR KEZ</b> çağrılır (kayıt oluşturulurken);
    /// append-only sözleşmesi gereği sonradan değiştirilmez — bu yüzden ayrı <c>Set*</c> metotları yoktur.</summary>
    public virtual void Fill(
        decimal? salePrice,
        string? currencyType,
        int? quantity,
        string? title,
        IEnumerable<(string Name, string Value)>? options,
        IEnumerable<(Guid MediaId, string? ContentHash)>? images,
        string? remoteReference,
        string? errorMessage = null)
    {
        ErrorMessage = errorMessage;
        SalePrice = salePrice;
        CurrencyType = currencyType;
        Quantity = quantity;
        Title = title;
        VariantOptions = options is null
            ? null
            : string.Join("; ", options.Select(o => o.Name + "=" + o.Value));
        Images = images is null
            ? null
            : string.Join(",", images.Select(i => i.MediaId.ToString("N") + ":" + (i.ContentHash ?? string.Empty)));
        RemoteReference = remoteReference;
    }

    public override string ToString()
    {
        return SellerStockCode + "@" + PushedAtUtc.ToString("O");
    }

    #endregion
}

/// <summary>Gönderimin türü — delil değerini belirler.</summary>
public enum N11ProductPushKind : byte
{
    /// <summary>Tam ürün push'u (başlık/görsel/nitelik dâhil).</summary>
    FullPush = 0,

    /// <summary>Yalnız fiyat/stok senkronu — içerik değişmez.</summary>
    PriceStockSync = 1,
}
