using System;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Reports;

public class ScrapReportFilterDto
{
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? VaultId { get; set; }
    /// <summary>Belirli bir hurda tanımı (null = tümü).</summary>
    public Guid? ScrapId { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
}

/// <summary>Hurda stok satırı — birim bazında net (HAS veya ödeme birimi).</summary>
public class ScrapStockRowDto
{
    public Guid UnitId { get; set; }
    public string? UnitCode { get; set; }
    public decimal InTotal { get; set; }
    public decimal OutTotal { get; set; }
    public decimal Net { get; set; }
}

/// <summary>Hurda hareket satırı.</summary>
public class ScrapMovementRowDto
{
    public DateTime VoucherDate { get; set; }
    public long VoucherNumber { get; set; }
    public ProcessType ProcessType { get; set; }
    public string? ProcessCode { get; set; }
    public string? CompanyCode { get; set; }
    public string? BranchCode { get; set; }
    public string? VaultCode { get; set; }
    public string? SubAccountCode { get; set; }
    public ProcessDirectionType Direction { get; set; }
    public string? CommodityCode { get; set; }
    public Guid UnitId { get; set; }
    public string? UnitCode { get; set; }
    public decimal Amount { get; set; }
    public decimal Factor { get; set; }
    public decimal Effect { get; set; }
    public decimal RunningBalance { get; set; }
    public string? Description { get; set; }
    public bool IsCarryForward { get; set; }
    public string? Source { get; set; }

    public decimal Devir => RunningBalance - Effect;
    public decimal Giren => Effect > 0 ? Effect : 0m;
    public decimal Cikan => Effect < 0 ? -Effect : 0m;
}
