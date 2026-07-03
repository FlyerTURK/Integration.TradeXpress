using System;
using System.Collections.Generic;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Cari-hesap-BAĞIMSIZ işlem raporu isteği. Şirket client'tan ALINMAZ — sunucuda daima
/// ICurrentCompany'den zorlanır (bilgi sızıntısı önlemi, BalanceSheet deseni). Branch/Vault
/// hiyerarşik opsiyonel (null = alt kırılımları topla). Sayfalama server-side.
/// </summary>
public class TransactionReportRequestDto
{
    public Guid? BranchId { get; set; }
    public Guid? VaultId { get; set; }

    /// <summary>Opsiyonel alt hesap filtresi (null = tüm hesaplar).</summary>
    public Guid? SubAccountId { get; set; }

    /// <summary>Rapor başlangıcı (dahil).</summary>
    public DateTime Start { get; set; }

    /// <summary>Rapor sonu (HARİÇ) — gün-sonu dahil için End.Date.AddDays(1) gönder.</summary>
    public DateTime EndExclusive { get; set; }

    /// <summary>İşlem tipi filtresi (null/boş = tümü).</summary>
    public List<ProcessType>? Types { get; set; }

    // ── Server-side sayfalama ──
    public int SkipCount { get; set; }
    public int MaxResultCount { get; set; } = 100;
}

/// <summary>İşlem raporu satırı — bir voucher satırının rapor projeksiyonu (ekstre benzeri + Hesap/AltHesap).</summary>
public class TransactionReportRowDto
{
    public DateTime VoucherDate { get; set; }
    public long VoucherNumber { get; set; }

    /// <summary>İşlem kısaltma kodu (<see cref="VoucherProcessCode"/> — ör. "NGP", "MGN").</summary>
    public string? ProcessCode { get; set; }

    public string? AccountCode { get; set; }
    public string? SubAccountCode { get; set; }
    public string? BranchCode { get; set; }
    public string? VaultCode { get; set; }

    public string? CommodityCode { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public decimal Total { get; set; }
    public string? MainUnitCode { get; set; }

    public decimal PayTotal { get; set; }
    public string? PayUnitCode { get; set; }

    public string? Description { get; set; }
    public string? CreatorName { get; set; }

    public override string ToString()
    {
        return $"{VoucherDate:d} #{VoucherNumber} {ProcessCode} {SubAccountCode ?? AccountCode} {Total:n2} {MainUnitCode}";
    }
}
