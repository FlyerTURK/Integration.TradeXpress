using System;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Mamül raporu kapsam filtresi. Hiyerarşik opsiyonel: Company seçili + Branch null → şirketin tümü;
/// Branch seçili + Vault null → şubenin tümü; Vault seçili → o kasa. (null = alt kırılımları topla.)
/// Mamül/Varyant opsiyonel: GoodId null → tüm mamüller; VariantId null → tüm varyantlar (yalnız GoodId seçiliyken anlamlı).
/// </summary>
public class GoodReportFilterDto
{
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? VaultId { get; set; }

    /// <summary>Belirli bir mamül tanımı (null = tümü).</summary>
    public Guid? GoodId { get; set; }

    /// <summary>Belirli bir varyant (null = tümü). Yalnız <see cref="GoodId"/> seçiliyken anlamlı.</summary>
    public Guid? VariantId { get; set; }

    /// <summary>Hareket raporu tarih aralığı (dahil). Stok raporunda yok sayılır (anlık).</summary>
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}

/// <summary>
/// Mamül stok satırı — mamül+varyant bazında adet (Quantity) ve miktar (Amount) net. Perakende:
/// milyem/HAS/işçilik YOK, Rezervasyon YOK, para (PayTotal) stok dışı. Birim = mamülün
/// <c>StockUnitCode</c>'u (voucher satırında MainUnitId mamülde daima boş; birim mamül seviyesinde).
/// </summary>
public class GoodStockRowDto
{
    public Guid? GoodId { get; set; }
    public string? GoodCode { get; set; }
    public string? GoodName { get; set; }

    public Guid? VariantId { get; set; }
    public string? VariantCode { get; set; }

    /// <summary>Stok birimi — mamülün StockUnitCode'u (SpecialCode; null olabilir).</summary>
    public string? UnitCode { get; set; }

    /// <summary>Toplam giriş adedi (Quantity).</summary>
    public decimal InQuantity { get; set; }
    /// <summary>Toplam çıkış adedi (Quantity).</summary>
    public decimal OutQuantity { get; set; }
    /// <summary>Net adet (InQuantity − OutQuantity).</summary>
    public decimal NetQuantity { get; set; }

    /// <summary>Toplam giriş miktarı (Amount).</summary>
    public decimal InAmount { get; set; }
    /// <summary>Toplam çıkış miktarı (Amount).</summary>
    public decimal OutAmount { get; set; }
    /// <summary>Net miktar (InAmount − OutAmount).</summary>
    public decimal NetAmount { get; set; }
}

/// <summary>
/// Mamül hareket satırı — adet (Quantity) birincil, miktar (Amount) ikincil. Tüm ödeme tipleri fiziksel
/// harekettir (Rezervasyon YOK). <see cref="Source"/>: ödeme tipi Türkçesi (Normal / Peşin / Bedelli /
/// İade / Emanet / Miktar / Diğer) veya "Devreden". Yürüyen bakiye anahtarı = (mamül, varyant).
/// </summary>
public class GoodMovementRowDto
{
    public DateTime VoucherDate { get; set; }
    public long VoucherNumber { get; set; }
    public ProcessType ProcessType { get; set; }
    public string? ProcessCode { get; set; }
    /// <summary>Ödeme tipi (Normal / Peşin / Bedelli / ...) veya "Devreden".</summary>
    public string Source { get; set; } = string.Empty;
    public string? CompanyCode { get; set; }
    public string? BranchCode { get; set; }
    public string? VaultCode { get; set; }
    public string? SubAccountCode { get; set; }
    public ProcessDirectionType Direction { get; set; }
    /// <summary>İşlemin mamül kodu (CommodityCode).</summary>
    public string? CommodityCode { get; set; }
    /// <summary>Varyant kodu snapshot (çok-varyantlı mamülde dolu).</summary>
    public string? VariantCode { get; set; }
    /// <summary>Stok birimi — mamülün StockUnitCode'u (null olabilir).</summary>
    public string? UnitCode { get; set; }

    /// <summary>Adet (Quantity) — perakende adet takibi.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Miktar (Amount) — stok-birimi miktarı.</summary>
    public decimal Amount { get; set; }

    /// <summary>İşaretli Quantity etkisi (+ giriş / − çıkış).</summary>
    public decimal Effect { get; set; }
    /// <summary>Bu satır dahil kümülatif Quantity bakiyesi (Son Adet).</summary>
    public decimal RunningBalance { get; set; }
    /// <summary>İşaretli Amount etkisi (+ giriş / − çıkış).</summary>
    public decimal EffectAmount { get; set; }
    /// <summary>Bu satır dahil kümülatif Amount bakiyesi (Son Miktar).</summary>
    public decimal RunningAmount { get; set; }

    public string? Description { get; set; }
    /// <summary>true = başlangıç tarihinden önceki birikimi gösteren devreden satırı.</summary>
    public bool IsCarryForward { get; set; }

    // ── Computed — Adet (Quantity) kolonları (birincil) ──
    /// <summary>Bu satırdan önceki Quantity bakiyesi (Devir = RunningBalance − Effect).</summary>
    public decimal Devir => RunningBalance - Effect;
    /// <summary>Giren adet (girişte pozitif; çıkışta 0).</summary>
    public decimal Giren => Effect > 0 ? Effect : 0m;
    /// <summary>Çıkan adet (çıkışta pozitif gösterim; girişte 0).</summary>
    public decimal Cikan => Effect < 0 ? -Effect : 0m;

    // ── Computed — Miktar (Amount) kolonları ──
    /// <summary>Bu satırdan önceki Amount bakiyesi (DevirAmount = RunningAmount − EffectAmount).</summary>
    public decimal DevirAmount => RunningAmount - EffectAmount;
    /// <summary>Giren miktar.</summary>
    public decimal GirenAmount => EffectAmount > 0 ? EffectAmount : 0m;
    /// <summary>Çıkan miktar.</summary>
    public decimal CikanAmount => EffectAmount < 0 ? -EffectAmount : 0m;
}
