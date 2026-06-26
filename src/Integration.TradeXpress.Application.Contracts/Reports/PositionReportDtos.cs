using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Pozisyon raporu kapsam filtresi. Hiyerarşik opsiyonel: Company seçili + Branch null → şirketin tümü;
/// Branch seçili + Vault null → şubenin tümü; Vault seçili → o kasa. (null = alt kırılımları topla.)
/// Tarih YOK — pozisyon ANLIK (tüm geçmiş ledger toplamı).
/// </summary>
public class PositionReportFilterDto
{
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? VaultId { get; set; }
}

/// <summary>
/// Bir birimin pozisyon satırı: net bakiye + bilanço birimine değerlenmiş karşılığı.
/// Bilanço (base) birim satırı görünür ama DURUM'a girmez (kendine karşı risk yok).
/// </summary>
public class PositionRowDto
{
    public Guid UnitId { get; set; }
    public string? UnitCode { get; set; }

    /// <summary>İşaretli net bakiye (+ alacak/long, − borç/short) — birimin kendi cinsinden.</summary>
    public decimal NetAmount { get; set; }

    /// <summary>Net'in bilanço birimine değerlenmiş karşılığı (alış kuru ile).</summary>
    public decimal ValuedBuy { get; set; }
    /// <summary>Net'in bilanço birimine değerlenmiş karşılığı (satış kuru ile).</summary>
    public decimal ValuedSell { get; set; }

    /// <summary>Bu birim bilanço (base) birimi mi — satır görünür ama DURUM dışı.</summary>
    public bool IsBaseUnit { get; set; }
    /// <summary>DURUM toplamına dahil mi (= base değil).</summary>
    public bool CountsInPosition { get; set; }

    /// <summary>Bu birim için değerleme kuru bulunamadı (feed yok) → Valued* anlamsız (yalnız net gösterilir).</summary>
    public bool MissingRate { get; set; }
}

/// <summary>
/// Pozisyon raporu sonucu: birim satırları + DURUM (base-dışı değerlenmiş net açık) bilanço biriminde.
/// </summary>
public class PositionReportResultDto
{
    public Guid BaseUnitId { get; set; }
    public string BaseCurrencyCode { get; set; } = string.Empty;

    public List<PositionRowDto> Rows { get; set; } = new();

    /// <summary>DURUM (net açık) — base-dışı satırların değerlenmiş toplamı, bilanço biriminde (alış).</summary>
    public decimal DurumBuy { get; set; }
    /// <summary>DURUM — satış kuruyla.</summary>
    public decimal DurumSell { get; set; }
}
