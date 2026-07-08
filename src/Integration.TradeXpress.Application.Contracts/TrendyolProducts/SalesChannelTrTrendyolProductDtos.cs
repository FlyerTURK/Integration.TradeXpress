using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>Trendyol kategori attribute değeri (id-bazlı; attributeValueId ile listeden ya da customValue ile serbest).</summary>
public class SalesChannelTrTrendyolProductAttributeDto
{
    public int AttributeId { get; set; }
    public int? AttributeValueId { get; set; }
    public string? CustomValue { get; set; }
}

/// <summary>Varyant Trendyol SKU kimlik/durum satırı (read-only; push + reconcile sonrası dolar). Barcode DONDURULMUŞ;
/// AttributeSnapshot UI'a taşınmaz (yeniden-bağlama imzası sunucu-içi kalır).</summary>
public class SalesChannelTrTrendyolProductSkuDto
{
    public Guid ProductVariantId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string StockCode { get; set; } = string.Empty;
    public long? RemoteContentId { get; set; }
    public int? LastSentQuantity { get; set; }
    public decimal? LastSentListPrice { get; set; }
    public decimal? LastSentSalePrice { get; set; }
}

/// <summary>Trendyol kanal-özel varyant override graf düğümü — ERP varyantının (SSOT: kod/ad/ERP fiyat/stok) Trendyol-scope
/// özelleştirmesi. LEFT JOIN: ERP varyant seti ⋈ kaydedilmiş kanal override. null override alanı = ERP'den devralınır.
/// Reçete (<see cref="RecipeLines"/>) kaydedilmişse ondan, yoksa ERP reçetesinden KLONLANIR (Id boş = henüz persist yok).
/// <see cref="NetCost"/>/<see cref="DerivedPrice"/> SALT-OKUNUR (GetAsync canlı hesaplar; save yoksayar).</summary>
public class SalesChannelTrTrendyolProductVariantGraphDto
{
    /// <summary>ERP varyantı (anchor; save'de kanal override başlığına eşlenir). Read-only kimlik.</summary>
    public Guid ProductVariantId { get; set; }

    /// <summary>ERP varyant kodu (SALT-OKUNUR görüntü; ERP SSOT).</summary>
    public string VariantCode { get; set; } = string.Empty;

    /// <summary>ERP varyant adı (SALT-OKUNUR görüntü; ERP SSOT).</summary>
    public string VariantName { get; set; } = string.Empty;

    /// <summary>Kanal-özel mutlak fiyat (opsiyonel; null = ERP/türetilmiş devralınır).</summary>
    public decimal? OverridePrice { get; set; }

    /// <summary>Override fiyatı para birimi (id-only; fiyat null ise yoksayılır).</summary>
    public Guid? OverridePriceCurrencyUnitId { get; set; }

    /// <summary>Kanal-özel stok (opsiyonel; null = ERP StockQuantity devralınır).</summary>
    public int? OverrideStock { get; set; }

    /// <summary>Varyant-başı marj (markup yüzdesi; null = marj yok). Türetilmiş fiyat = NetCost × (1 + Margin/100).</summary>
    public decimal? Margin { get; set; }

    /// <summary>Kanal-özel reçete satırları (ERP reçetesinden klonlanır, sonra bağımsız) — Product reçetesiyle AYNI
    /// DTO tipi (ProductRecipePanel bunu tüketir). Id + IsDeleted diff; save'de kanal reçete tablosuna yazılır.</summary>
    public List<ProductRecipeLineGraphDto> RecipeLines { get; set; } = new();

    /// <summary>Reçetenin CANLI net maliyeti — ülke birimine rebase (SALT-OKUNUR; GetAsync hesaplar, save yoksayar).</summary>
    public decimal? NetCost { get; set; }

    /// <summary>Net maliyet para birimi kodu (ülke birimi; SALT-OKUNUR).</summary>
    public string NetCostCurrency { get; set; } = string.Empty;

    /// <summary>Net maliyet satırlarından biri kur/birim-eksik mi (SALT-OKUNUR UI uyarısı).</summary>
    public bool NetCostMissingRate { get; set; }

    /// <summary>Türetilmiş fiyat = NetCost × (1 + (Margin ?? 0)/100) [MARKUP] (SALT-OKUNUR; NetCost null ise null).</summary>
    public decimal? DerivedPrice { get; set; }
}

/// <summary>Trendyol ürün listelemesi — tam okuma modeli (edit + durum görüntüsü). Ürün grafının parçası olarak da
/// kullanılır (ürün 'Kaydet'inde birlikte kaydedilir): <see cref="ClientKey"/> in-memory kimlik, <see cref="IsDeleted"/>
/// soft-delete işareti (graf diff). Kaydedilmiş kayıtta <see cref="Id"/> dolu; yeni satırda boş (N11 DTO paritesi).</summary>
public class SalesChannelTrTrendyolProductDto
{
    public Guid Id { get; set; }

    /// <summary>İstemci-taraflı graf kimliği (yeni satırda Id yok; graf diff için).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    /// <summary>Graf soft-delete işareti — ürün save'inde silinecek satır.</summary>
    public bool IsDeleted { get; set; }

    public Guid ProductId { get; set; }
    public Guid SalesChannelId { get; set; }

    /// <summary>Varyant grup anahtarı (productMainId — "{ÜrünKodu}-{SequenceNo}", read-only/frozen).</summary>
    public string ProductMainId { get; set; } = string.Empty;

    /// <summary>Kayıt sırası (read-only; soft-delete dahil max+1).</summary>
    public int SequenceNo { get; set; }

    public string CategoryId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string BrandId { get; set; } = string.Empty;
    public string? BrandName { get; set; }
    public int VatRate { get; set; } = 20;
    public int? CargoCompanyId { get; set; }
    public decimal? DimensionalWeight { get; set; }
    public string? Description { get; set; }
    public int? DeliveryDuration { get; set; }
    public TrendyolFastDeliveryType? FastDeliveryType { get; set; }
    public List<SalesChannelTrTrendyolProductAttributeDto> Attributes { get; set; } = new();

    /// <summary>Varyant SKU kimlik/durum satırları (read-only; push + reconcile sonrası dolar).</summary>
    public List<SalesChannelTrTrendyolProductSkuDto> Skus { get; set; } = new();

    /// <summary>Kanal-özel varyant override'ları (fiyat/stok/marj + reçete graf düğümleri) — ERP varyant seti ⋈
    /// kaydedilmiş override (LEFT JOIN). Ürün 'Kaydet'inde birlikte kaydedilir. NetCost/DerivedPrice SALT-OKUNUR.</summary>
    public List<SalesChannelTrTrendyolProductVariantGraphDto> Variants { get; set; } = new();

    // Trendyol senkron durumu (read-only; submit/refresh sonrası dolar).
    public string? BatchRequestId { get; set; }
    public string? LastBatchRequestType { get; set; }
    public string? Status { get; set; }
    public int? FailedItemCount { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastError { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Create/Update ortak düzenlenebilir alanları.</summary>
public interface ISalesChannelTrTrendyolProductInput
{
    string CategoryId { get; }
    string? CategoryName { get; }
    string BrandId { get; }
    string? BrandName { get; }
    int VatRate { get; }
    int? CargoCompanyId { get; }
    decimal? DimensionalWeight { get; }
    string? Description { get; }
    int? DeliveryDuration { get; }
    TrendyolFastDeliveryType? FastDeliveryType { get; }
    bool IsActive { get; }
    List<SalesChannelTrTrendyolProductAttributeDto> Attributes { get; }

    /// <summary>Kanal-özel varyant override grafı (fiyat/stok/marj + reçete) — kanal-ürünle birlikte kaydedilir.</summary>
    List<SalesChannelTrTrendyolProductVariantGraphDto> Variants { get; }
}

/// <summary>Listeleme oluşturma — ürün + kanal (create-only; şirket sunucuda zorlanır).</summary>
public class SalesChannelTrTrendyolProductCreateDto : ISalesChannelTrTrendyolProductInput
{
    public Guid ProductId { get; set; }
    public Guid SalesChannelId { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string BrandId { get; set; } = string.Empty;
    public string? BrandName { get; set; }
    public int VatRate { get; set; } = 20;
    public int? CargoCompanyId { get; set; }
    public decimal? DimensionalWeight { get; set; }
    public string? Description { get; set; }
    public int? DeliveryDuration { get; set; }
    public TrendyolFastDeliveryType? FastDeliveryType { get; set; }
    public bool IsActive { get; set; } = true;
    public List<SalesChannelTrTrendyolProductAttributeDto> Attributes { get; set; } = new();
    public List<SalesChannelTrTrendyolProductVariantGraphDto> Variants { get; set; } = new();
}

/// <summary>Listeleme güncelleme — ürün/kanal set-once (route'taki id kimliktir).</summary>
public class SalesChannelTrTrendyolProductUpdateDto : ISalesChannelTrTrendyolProductInput
{
    public string CategoryId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string BrandId { get; set; } = string.Empty;
    public string? BrandName { get; set; }
    public int VatRate { get; set; } = 20;
    public int? CargoCompanyId { get; set; }
    public decimal? DimensionalWeight { get; set; }
    public string? Description { get; set; }
    public int? DeliveryDuration { get; set; }
    public TrendyolFastDeliveryType? FastDeliveryType { get; set; }
    public bool IsActive { get; set; } = true;
    public List<SalesChannelTrTrendyolProductAttributeDto> Attributes { get; set; } = new();
    public List<SalesChannelTrTrendyolProductVariantGraphDto> Variants { get; set; } = new();
}

/// <summary>
/// Trendyol ürün listeleme — bir ERP ürününü bir Trendyol kanalında listeler + Trendyol'a ASENKRON push eder.
/// Yapılandırma (kategori/marka/KDV/kargo/attribute) bizde tutulur; <see cref="PushToTrendyolAsync"/> ürünü +
/// varyantlarını gönderir (batch id döner), <see cref="RefreshStatusAsync"/> batch durumunu çeker. Company-owned.
/// </summary>
public interface ISalesChannelTrTrendyolProductAppService : IApplicationService
{
    /// <summary>Bir ÜRÜNE ait tüm Trendyol kanal ürünleri (ürün-merkezli drill). Aynı kanalda birden fazla kayıt
    /// OLABİLİR (N11 ile aynı 2026-07-07 kararı); kanal set-once (değiştirilemez).</summary>
    Task<List<SalesChannelTrTrendyolProductDto>> GetListForProductAsync(Guid productId);

    Task<SalesChannelTrTrendyolProductDto> GetAsync(Guid id);

    Task<SalesChannelTrTrendyolProductDto> CreateAsync(SalesChannelTrTrendyolProductCreateDto input);

    Task<SalesChannelTrTrendyolProductDto> UpdateAsync(Guid id, SalesChannelTrTrendyolProductUpdateDto input);

    /// <summary>Yalnız yerel siler (Trendyol'da pasifleştirme ayrı; ürün Trendyol'da kalır).</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Listelemeyi Trendyol'a gönderir (async create). Batch id kaydedilir; durum PROCESSING olur.</summary>
    Task<SalesChannelTrTrendyolProductDto> PushToTrendyolAsync(Guid id);

    /// <summary>Kaydedilmiş batch id ile Trendyol'dan işlem durumunu çeker + günceller (COMPLETED/FAILED).</summary>
    Task<SalesChannelTrTrendyolProductDto> RefreshStatusAsync(Guid id);
}
