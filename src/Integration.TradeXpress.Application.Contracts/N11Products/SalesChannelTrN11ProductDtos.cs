using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.N11Products;

/// <summary>N11 kategori attribute değeri (name/value).</summary>
public class SalesChannelTrN11ProductAttributeDto
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>N11 Seyahat özel bilgi (key/value; key=TurProgrami/IptalIadeKosullari/EkHizmetler).</summary>
public class SalesChannelTrN11ProductSpecialInfoDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>Varyant SKU kimlik/durum satırı (read-only; push + stok/fiyat senkronunda dolar). UI görünürlük +
/// senkron durumu; AttributeSnapshot UI'a taşınmaz (sipariş eşleme sunucu-içi kalır).</summary>
public class SalesChannelTrN11ProductSkuDto
{
    public Guid ProductVariantId { get; set; }
    public string SellerStockCode { get; set; } = string.Empty;
    public long? N11SkuId { get; set; }
    public long? N11Version { get; set; }
    public int? LastSentQuantity { get; set; }
    public decimal? LastSentOptionPrice { get; set; }
}

/// <summary>N11 push ÖNİZLEMESİ (read-only) — bu listelemede N11'e GİDECEK varyantlar + görseller. Kaynak ERP
/// ürünü (SSOT); N11 formunda yalnız görünürlük (tanım ERP ürün formunda yapılır). Push anındaki fiili veri.</summary>
public class N11PushPreviewDto
{
    public List<N11PreviewVariantDto> Variants { get; set; } = new();
    public List<N11PreviewImageDto> Images { get; set; } = new();
}

/// <summary>Önizleme varyant satırı — N11'e stockItem olarak gidecek (kod/ad/stok/fiyat + seçenek özeti).</summary>
public class N11PreviewVariantDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public decimal? SalePrice { get; set; }

    /// <summary>Seçenek özeti ("Renk: Kırmızı; Beden: M") — varyant eksenleri (name/value).</summary>
    public string Options { get; set; } = string.Empty;
}

/// <summary>Önizleme görsel satırı — N11'e gidecek görsel (kaynak metni + ana bayrağı + varsa küçük önizleme).</summary>
public class N11PreviewImageDto
{
    public string Source { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    /// <summary>Yüklenmiş (blob) görsel için thumbnail data-URL'i; URL kaynaklı görselde boş (dış link).</summary>
    public string? PreviewDataUrl { get; set; }
}

/// <summary>N11 ürün listelemesi — tam okuma modeli (edit + durum görüntüsü).</summary>
public class SalesChannelTrN11ProductDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid SalesChannelId { get; set; }

    /// <summary>N11 upsert kimliği ("{ÜrünKodu}-{Sıra}") — sunucu üretir (read-only; create/update input'unda YOK).</summary>
    public string SellerCode { get; set; } = string.Empty;

    /// <summary>Kayıt sırası (read-only) — varyant stok kodu eklerinde kullanılır.</summary>
    public int SequenceNo { get; set; }
    public string CategoryExternalId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public N11ProductCondition Condition { get; set; }
    public string ShipmentTemplateName { get; set; } = string.Empty;
    public bool Domestic { get; set; }
    public int PreparingDay { get; set; }
    public int? MaxPurchaseQuantity { get; set; }

    /// <summary>N11 satıcı notu (kanal-özel kısa düz not; opsiyonel).</summary>
    public string? SellerNote { get; set; }

    /// <summary>N11 kanal-özel açıklama (HTML; opsiyonel). Boşsa push'ta ürün açıklaması devralınır.</summary>
    public string? Description { get; set; }
    public List<SalesChannelTrN11ProductAttributeDto> Attributes { get; set; } = new();
    public List<SalesChannelTrN11ProductSpecialInfoDto> SpecialInfo { get; set; } = new();


    /// <summary>Varyant SKU kimlik/durum satırları (read-only; push + stok/fiyat senkronunda dolar).</summary>
    public List<SalesChannelTrN11ProductSkuDto> Skus { get; set; } = new();

    // N11 senkron durumu (read-only; push sonrası dolar).
    public long? N11ProductId { get; set; }
    public string? SaleStatus { get; set; }
    public string? ApprovalStatus { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastError { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Push sonrası eşitleme uyarıları (LOKALİZE; ör. N11 kategoriyi değiştirdi) — SALT anlık görüntü,
    /// persist edilmez; yalnız PushToN11Async dönüşünde dolar (UI uyarı toast'ları gösterir).</summary>
    public List<string> SyncWarnings { get; set; } = new();
}

/// <summary>Create/Update ortak düzenlenebilir alanları.</summary>
public interface ISalesChannelTrN11ProductInput
{
    string CategoryExternalId { get; }
    string? CategoryName { get; }
    N11ProductCondition Condition { get; }
    string ShipmentTemplateName { get; }
    bool Domestic { get; }
    int PreparingDay { get; }
    int? MaxPurchaseQuantity { get; }
    bool IsActive { get; }
    string? SellerNote { get; }
    string? Description { get; }
    List<SalesChannelTrN11ProductAttributeDto> Attributes { get; }
    List<SalesChannelTrN11ProductSpecialInfoDto> SpecialInfo { get; }
}

/// <summary>Listeleme oluşturma — ürün + kanal (create-only; şirket sunucuda zorlanır).</summary>
public class SalesChannelTrN11ProductCreateDto : ISalesChannelTrN11ProductInput
{
    public Guid ProductId { get; set; }
    public Guid SalesChannelId { get; set; }
    public string CategoryExternalId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public N11ProductCondition Condition { get; set; } = N11ProductCondition.New;
    public string ShipmentTemplateName { get; set; } = string.Empty;
    public bool Domestic { get; set; } = true;
    public int PreparingDay { get; set; } = 1;
    public int? MaxPurchaseQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SellerNote { get; set; }
    public string? Description { get; set; }
    public List<SalesChannelTrN11ProductAttributeDto> Attributes { get; set; } = new();
    public List<SalesChannelTrN11ProductSpecialInfoDto> SpecialInfo { get; set; } = new();
}

/// <summary>Listeleme güncelleme — ürün/kanal set-once (route'taki id kimliktir).</summary>
public class SalesChannelTrN11ProductUpdateDto : ISalesChannelTrN11ProductInput
{
    public string CategoryExternalId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public N11ProductCondition Condition { get; set; } = N11ProductCondition.New;
    public string ShipmentTemplateName { get; set; } = string.Empty;
    public bool Domestic { get; set; } = true;
    public int PreparingDay { get; set; } = 1;
    public int? MaxPurchaseQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SellerNote { get; set; }
    public string? Description { get; set; }
    public List<SalesChannelTrN11ProductAttributeDto> Attributes { get; set; } = new();
    public List<SalesChannelTrN11ProductSpecialInfoDto> SpecialInfo { get; set; } = new();
}

/// <summary>
/// N11 ürün listeleme — bir ERP ürününü bir N11 kanalında listeler + N11'e push eder (SaveProduct). Company-owned.
/// Listeleme yapılandırması (kategori/attribute/kargo şablonu/condition/özel bilgi) bizde tutulur; push ürünün
/// varyantları (stockItems) + fiyat/stok/görseliyle birlikte N11'e gider.
/// </summary>
public interface ISalesChannelTrN11ProductAppService : IApplicationService
{
    /// <summary>Bir ÜRÜNE ait tüm N11 kanal ürünleri (ürün-merkezli drill). Aynı kanalda birden fazla kayıt
    /// OLABİLİR (2026-07-07 kullanıcı kararı); kanal set-once (değiştirilemez).</summary>
    Task<List<SalesChannelTrN11ProductDto>> GetListForProductAsync(Guid productId);

    /// <summary>Bir KANALA ait tüm ürün listelemeleri (kanal-merkezli yönetim görünümü).</summary>
    Task<List<SalesChannelTrN11ProductDto>> GetListForChannelAsync(Guid salesChannelId);

    Task<SalesChannelTrN11ProductDto> GetAsync(Guid id);

    Task<SalesChannelTrN11ProductDto> CreateAsync(SalesChannelTrN11ProductCreateDto input);

    Task<SalesChannelTrN11ProductDto> UpdateAsync(Guid id, SalesChannelTrN11ProductUpdateDto input);

    /// <summary>Yalnız yerel siler (N11'de pasifleştirme ayrı; ürün N11'de kalır).</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Listelemeyi N11'e gönderir (SaveProduct): ürün + varyant + fiyat/stok/görsel. Durumu günceller + döner.</summary>
    Task<SalesChannelTrN11ProductDto> PushToN11Async(Guid id);

    /// <summary>Yalnız stok+fiyatı N11'e gönderir (UpdateProductBasic — Faz 2, hafif): tam SaveProduct'a gerek
    /// olmadan değişen varyantların adet/fiyatını günceller. Önce N11'den okur (eksik SKU id doldurma + version
    /// drift uyarısı), yalnız DEĞİŞEN varyantları gönderir; değişiklik yoksa no-op. Yapı/varyant-seti değiştiyse
    /// bu YETMEZ — tam <see cref="PushToN11Async"/> gerekir.</summary>
    Task<SalesChannelTrN11ProductDto> SyncStockAndPriceAsync(Guid id);

    /// <summary>Push önizlemesi (read-only): bu listelemede N11'e GİDECEK varyantlar + görseller (kaynak ERP ürünü).</summary>
    Task<N11PushPreviewDto> GetPushPreviewAsync(Guid id);
}
