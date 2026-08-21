using System;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Nakit raporları kapsam filtresi. Hiyerarşik opsiyonel: Company seçili + Branch null → şirketin tümü;
/// Branch seçili + Vault null → şubenin tümü; Vault seçili → o kasa. (null = alt kırılımları topla.)
/// </summary>
public class CashReportFilterDto
{
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? VaultId { get; set; }

    /// <summary>Belirli bir nakit tanımı (null = tümü, stok raporunda anlamlı; hareket raporunda seçmek zorunlu değil ama önerilir).</summary>
    public Guid? CashId { get; set; }

    /// <summary>Hareket raporu tarih aralığı (dahil). Stok raporunda yok sayılır (anlık).</summary>
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}

/// <summary>Nakit stok satırı — para birimi bazında net (Giriş − Çıkış).</summary>
public class CashStockRowDto
{
    public Guid UnitId { get; set; }
    public string? UnitCode { get; set; }
    public decimal InTotal { get; set; }
    public decimal OutTotal { get; set; }
    public decimal Net { get; set; }
}

/// <summary>Nakit hareket satırı — bir satırın nakit tarafı. <see cref="CashAmount"/> işaretli (+ kasaya girer, − çıkar).</summary>
public class CashMovementRowDto
{
    public DateTime VoucherDate { get; set; }
    public long VoucherNumber { get; set; }
    public ProcessType ProcessType { get; set; }
    /// <summary>Nakit tarafının kaynağı: "Nakit" (Cash sol taraf) veya "Peşin" (sağ taraf). Devreden satırı için "Devreden".</summary>
    public string Source { get; set; } = string.Empty;
    public string? CompanyCode { get; set; }
    public string? BranchCode { get; set; }
    public string? VaultCode { get; set; }
    public string? SubAccountCode { get; set; }
    public ProcessDirectionType Direction { get; set; }
    /// <summary>İşlem kısaltma kodu (VoucherProcessCode.Code).</summary>
    public string? ProcessCode { get; set; }
    /// <summary>İşlemin ana malı (CommodityCode) — nakit tanım kodu değil.</summary>
    public string? CommodityCode { get; set; }
    public Guid UnitId { get; set; }
    public string? UnitCode { get; set; }
    /// <summary>İşaretli nakit etkisi (+ kasaya giriş / − çıkış).</summary>
    public decimal CashAmount { get; set; }
    /// <summary>Bu satır dahil kümülatif bakiye (Son Durum).</summary>
    public decimal RunningBalance { get; set; }
    public string? Description { get; set; }
    /// <summary>true = başlangıç tarihinden önceki birikimi gösteren devreden satırı (gerçek işlem değil).</summary>
    public bool IsCarryForward { get; set; }

    // Computed — grid kolonları için
    /// <summary>Bu satırdan önceki bakiye (Devir = RunningBalance − CashAmount).</summary>
    public decimal Devir => RunningBalance - CashAmount;
    /// <summary>Giren (kasaya giren tutar; çıkışta 0).</summary>
    public decimal Giren => CashAmount > 0 ? CashAmount : 0m;
    /// <summary>Çıkan (kasadan çıkan tutar, pozitif gösterim; girişte 0).</summary>
    public decimal Cikan => CashAmount < 0 ? -CashAmount : 0m;
}
