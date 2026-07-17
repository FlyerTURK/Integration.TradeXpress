using System;

namespace Integration.TradeXpress.Metals;

/// <summary>
/// Reçete paneli için Maden Varyantı seçim DTO'su (CommodityId ve VariantId yassılaştırılmış).
/// Gösterim (DisplayText) formatı: METALCODE / VARIANTCODE
/// </summary>
public class MetalVariantLookupDto
{
    public Guid CommodityId { get; set; }
    public string MetalCode { get; set; } = string.Empty;
    public string MetalName { get; set; } = string.Empty;
    
    public Guid VariantId { get; set; }
    public string VariantCode { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;

    public Integration.TradeXpress.Vouchers.MetalLaborType LaborType { get; set; }
    public decimal EntryLabor { get; set; }
    public Guid? EntryLaborUnitId { get; set; }
    public decimal ExitLabor { get; set; }
    public Guid? ExitLaborUnitId { get; set; }

    /// <summary>Metal adetli mi (sikke/parça).</summary>
    public bool IsQuantity { get; set; }
    /// <summary>Sabit adet-gram karşılığı (adetliyse Adet→Gram dönüşüm).</summary>
    public decimal StableQuantity { get; set; }

    public string DisplayText => $"{MetalCode} / {VariantCode}";

    public override string ToString()
    {
        return DisplayText;
    }
}
