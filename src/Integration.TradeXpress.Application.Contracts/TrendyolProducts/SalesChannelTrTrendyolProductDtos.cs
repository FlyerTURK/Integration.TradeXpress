using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

/// <summary>Trendyol ürün listelemesi — tam okuma modeli (edit + durum görüntüsü).</summary>
public class SalesChannelTrTrendyolProductDto
{
    public Guid Id { get; set; }
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
