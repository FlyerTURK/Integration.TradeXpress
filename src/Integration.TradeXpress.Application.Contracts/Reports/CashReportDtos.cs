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

/// <summary>Nakit hareket satırı — bir satırın nakit bacağı. <see cref="CashAmount"/> işaretli (+ kasaya girer, − çıkar).</summary>
public class CashMovementRowDto
{
    public DateTime VoucherDate { get; set; }
    public long VoucherNumber { get; set; }
    public ProcessType ProcessType { get; set; }
    /// <summary>Nakit bacağı kaynağı: "Nakit" (Cash sol bacak) veya "Peşin" (sağ bacak).</summary>
    public string Source { get; set; } = string.Empty;
    public string? VaultCode { get; set; }
    public string? SubAccountCode { get; set; }
    public ProcessDirectionType Direction { get; set; }
    public string? CashCode { get; set; }
    public Guid UnitId { get; set; }
    public string? UnitCode { get; set; }
    /// <summary>İşaretli nakit etkisi (+ kasaya giriş / − çıkış).</summary>
    public decimal CashAmount { get; set; }
    public string? Description { get; set; }
}
