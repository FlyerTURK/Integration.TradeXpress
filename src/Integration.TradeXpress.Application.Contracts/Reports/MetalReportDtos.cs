using System;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Maden raporu kapsam filtresi. Hiyerarşik opsiyonel: Company seçili + Branch null → şirketin tümü;
/// Branch seçili + Vault null → şubenin tümü; Vault seçili → o kasa. (null = alt kırılımları topla.)
/// </summary>
public class MetalReportFilterDto
{
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? VaultId { get; set; }

    /// <summary>Belirli bir maden tanımı (null = tümü).</summary>
    public Guid? MetalId { get; set; }

    /// <summary>Hareket raporu tarih aralığı (dahil). Stok raporunda yok sayılır (anlık).</summary>
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}

/// <summary>
/// Maden stok satırı — birim bazında miktar (Amount = ağırlık, Quantity = adet) ve net.
/// </summary>
public class MetalStockRowDto
{
    public Guid? MetalId { get; set; }
    public string? MetalCode { get; set; }
    public string? MetalName { get; set; }

    public Guid UnitId { get; set; }
    public string? UnitCode { get; set; }

    /// <summary>Toplam giriş ağırlığı (Amount).</summary>
    public decimal InAmount { get; set; }
    /// <summary>Toplam çıkış ağırlığı (Amount).</summary>
    public decimal OutAmount { get; set; }
    /// <summary>Net ağırlık (InAmount − OutAmount).</summary>
    public decimal NetAmount { get; set; }

    /// <summary>Toplam giriş adedi (Quantity).</summary>
    public decimal InQuantity { get; set; }
    /// <summary>Toplam çıkış adedi (Quantity).</summary>
    public decimal OutQuantity { get; set; }
    /// <summary>Net adet (InQuantity − OutQuantity).</summary>
    public decimal NetQuantity { get; set; }

    // ── Rezervasyon (ProcessPaymentType.Reservation) — fiziksel Net'e GİRMEZ, ayrı sayaç ──
    /// <summary>Çıkış rezervasyonu net adedi — müşteriye ayrılan (kullanılabilirden düşer).</summary>
    public decimal ReservedOutQuantity { get; set; }
    /// <summary>Çıkış rezervasyonu net ağırlığı (Amount).</summary>
    public decimal ReservedOutAmount { get; set; }
    /// <summary>Giriş rezervasyonu net adedi — tedarikçiden beklenen (bilgi amaçlı; kullanılabilire eklenmez).</summary>
    public decimal ReservedInQuantity { get; set; }
    /// <summary>Giriş rezervasyonu net ağırlığı (Amount).</summary>
    public decimal ReservedInAmount { get; set; }
    /// <summary>Kullanılabilir adet = NetQuantity − ReservedOutQuantity.</summary>
    public decimal AvailableQuantity { get; set; }
    /// <summary>Kullanılabilir ağırlık = NetAmount − ReservedOutAmount.</summary>
    public decimal AvailableAmount { get; set; }
}

/// <summary>
/// Maden hareket satırı. Tüm ödeme tipleri dahil; fiziksel maden miktarı (Amount @ MainUnit) esaslıdır.
/// <see cref="Source"/>: ödeme tipi Türkçesi (Normal / Peşin / Bedelli / İade / Emanet / Miktar / Rezervasyon) veya "Devreden".
/// Rezervasyon satırları listede görünür ama fiziksel kümülatife (RunningBalance/RunningQty) KATILMAZ.
/// </summary>
public class MetalMovementRowDto
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
    /// <summary>İşlemin maden kodu (CommodityCode).</summary>
    public string? CommodityCode { get; set; }
    public Guid UnitId { get; set; }
    public string? UnitCode { get; set; }
    /// <summary>Adet (Quantity) — maden adet takibi.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Ağırlık miktarı (Amount) — maden gramajı.</summary>
    public decimal Amount { get; set; }
    /// <summary>Milyem faktörü.</summary>
    public decimal Factor { get; set; }
    /// <summary>İşaretli Amount etkisi (+ giriş / − çıkış).</summary>
    public decimal Effect { get; set; }
    /// <summary>Bu satır dahil kümülatif Amount bakiyesi (Son Durum).</summary>
    public decimal RunningBalance { get; set; }
    /// <summary>İşaretli Quantity etkisi (+ giriş / − çıkış).</summary>
    public decimal EffectQty { get; set; }
    /// <summary>Bu satır dahil kümülatif Quantity bakiyesi.</summary>
    public decimal RunningQty { get; set; }
    public string? Description { get; set; }
    /// <summary>true = başlangıç tarihinden önceki birikimi gösteren devreden satırı.</summary>
    public bool IsCarryForward { get; set; }
    /// <summary>true = rezervasyon satırı — Giren/Çıkan rezerve miktarı gösterir ama
    /// fiziksel kümülatife (RunningBalance/RunningQty) katılmaz.</summary>
    public bool IsReservation { get; set; }

    // Computed — grid kolonları için
    /// <summary>Bu satırdan önceki Amount bakiyesi (Devir = RunningBalance − Effect;
    /// rezervasyonda Effect kümülatife girmediği için Devir = RunningBalance).</summary>
    public decimal Devir => RunningBalance - (IsReservation ? 0m : Effect);
    /// <summary>Giren ağırlık (girişte pozitif; çıkışta 0).</summary>
    public decimal Giren => Effect > 0 ? Effect : 0m;
    /// <summary>Çıkan ağırlık (çıkışta pozitif gösterim; girişte 0).</summary>
    public decimal Cikan => Effect < 0 ? -Effect : 0m;

    /// <summary>Bu satırdan önceki Quantity bakiyesi.</summary>
    public decimal DevirQty => RunningQty - (IsReservation ? 0m : EffectQty);
    /// <summary>Giren adet.</summary>
    public decimal GirenQty => EffectQty > 0 ? EffectQty : 0m;
    /// <summary>Çıkan adet.</summary>
    public decimal CikanQty => EffectQty < 0 ? -EffectQty : 0m;
}
