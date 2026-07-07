using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>Trendyol kategori attribute değeri (id-bazlı; attributeValueId ile listeden ya da customValue ile serbest).</summary>
public class TrendyolListingAttributeDto
{
    public int AttributeId { get; set; }
    public int? AttributeValueId { get; set; }
    public string? CustomValue { get; set; }
}

/// <summary>Trendyol ürün listelemesi — tam okuma modeli (edit + durum görüntüsü).</summary>
public class TrendyolProductListingDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid SalesChannelId { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string BrandId { get; set; } = string.Empty;
    public int VatRate { get; set; } = 20;
    public int? CargoCompanyId { get; set; }
    public decimal? DimensionalWeight { get; set; }
    public List<TrendyolListingAttributeDto> Attributes { get; set; } = new();

    // Trendyol senkron durumu (read-only; submit/refresh sonrası dolar).
    public string? BatchRequestId { get; set; }
    public string? Status { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastError { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Create/Update ortak düzenlenebilir alanları.</summary>
public interface ITrendyolProductListingInput
{
    string CategoryId { get; }
    string? CategoryName { get; }
    string BrandId { get; }
    int VatRate { get; }
    int? CargoCompanyId { get; }
    decimal? DimensionalWeight { get; }
    bool IsActive { get; }
    List<TrendyolListingAttributeDto> Attributes { get; }
}

/// <summary>Listeleme oluşturma — ürün + kanal (create-only; şirket sunucuda zorlanır).</summary>
public class TrendyolProductListingCreateDto : ITrendyolProductListingInput
{
    public Guid ProductId { get; set; }
    public Guid SalesChannelId { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string BrandId { get; set; } = string.Empty;
    public int VatRate { get; set; } = 20;
    public int? CargoCompanyId { get; set; }
    public decimal? DimensionalWeight { get; set; }
    public bool IsActive { get; set; } = true;
    public List<TrendyolListingAttributeDto> Attributes { get; set; } = new();
}

/// <summary>Listeleme güncelleme — ürün/kanal set-once (route'taki id kimliktir).</summary>
public class TrendyolProductListingUpdateDto : ITrendyolProductListingInput
{
    public string CategoryId { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string BrandId { get; set; } = string.Empty;
    public int VatRate { get; set; } = 20;
    public int? CargoCompanyId { get; set; }
    public decimal? DimensionalWeight { get; set; }
    public bool IsActive { get; set; } = true;
    public List<TrendyolListingAttributeDto> Attributes { get; set; } = new();
}

/// <summary>
/// Trendyol ürün listeleme — bir ERP ürününü bir Trendyol kanalında listeler + Trendyol'a ASENKRON push eder.
/// Yapılandırma (kategori/marka/KDV/kargo/attribute) bizde tutulur; <see cref="ListToTrendyolAsync"/> ürünü +
/// varyantlarını gönderir (batch id döner), <see cref="RefreshStatusAsync"/> batch durumunu çeker. Company-owned.
/// </summary>
public interface ITrendyolProductListingAppService : IApplicationService
{
    /// <summary>Bir ürünün bir kanaldaki listelemesi (yoksa null).</summary>
    Task<TrendyolProductListingDto?> GetForProductAsync(Guid productId, Guid salesChannelId);

    Task<TrendyolProductListingDto> GetAsync(Guid id);

    Task<TrendyolProductListingDto> CreateAsync(TrendyolProductListingCreateDto input);

    Task<TrendyolProductListingDto> UpdateAsync(Guid id, TrendyolProductListingUpdateDto input);

    /// <summary>Yalnız yerel siler (Trendyol'da pasifleştirme ayrı; ürün Trendyol'da kalır).</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Listelemeyi Trendyol'a gönderir (async create). Batch id kaydedilir; durum PROCESSING olur.</summary>
    Task<TrendyolProductListingDto> ListToTrendyolAsync(Guid id);

    /// <summary>Kaydedilmiş batch id ile Trendyol'dan işlem durumunu çeker + günceller (COMPLETED/FAILED).</summary>
    Task<TrendyolProductListingDto> RefreshStatusAsync(Guid id);
}
