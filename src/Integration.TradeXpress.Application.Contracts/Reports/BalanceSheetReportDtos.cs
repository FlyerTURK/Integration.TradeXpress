using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Reports;

/// <summary>Bilanço kapsam kademesi — kullanıcı toolbar switch'iyle seçer.</summary>
public enum BalanceSheetScope
{
    /// <summary>Çalışılan ŞUBE bazında (Branch.Base biriminde).</summary>
    Branch = 0,
    /// <summary>Çalışılan ŞİRKET konsolide (Company.Base biriminde; tüm şubeleri toplar).</summary>
    Company = 1,
}

/// <summary>
/// Bilanço hesaplama filtresi. Kapsam (Branch/Company) + tarih (AsOf — o güne kadarki snapshot). CompanyId DAİMA
/// sunucuda working-context'ten zorlanır (sızıntı önleme, pozisyonla aynı). Branch scope'ta client working şubeyi
/// gönderir; sunucu şirkete ait olduğunu doğrular.
/// </summary>
public class BalanceSheetReportFilterDto
{
    public BalanceSheetScope Scope { get; set; } = BalanceSheetScope.Branch;
    public Guid? BranchId { get; set; }
    public DateTime AsOf { get; set; }
}

/// <summary>Bilanço detay satırı: bir kategori×birim → kendi miktarı + base'e değerlenmiş Net.</summary>
public class BalanceSheetDetailRowDto
{
    public string Category { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public string? UnitCode { get; set; }

    /// <summary>Birimin kendi cinsinden bakiye/miktar.</summary>
    public decimal Amount { get; set; }
    /// <summary>Bu birimi base'e çeviren efektif değerleme kuru (Net = Amount × ValuationRate). ERPPRO Kur1/Kur2
    /// çaprazının tek-kur karşılığı; şeffaflık/denetim için satırda gösterilir. MissingRate ise 0.</summary>
    public decimal ValuationRate { get; set; }
    /// <summary>Base (bilanço) birimine değerlenmiş karşılığı (alış kuru).</summary>
    public decimal Net { get; set; }
    /// <summary>Değerleme kuru yok (feed eksik) → Net anlamsız (yalnız Amount anlamlı).</summary>
    public bool MissingRate { get; set; }
}

/// <summary>Kategori toplamı — PIVOT sunum + TOPLAM için.</summary>
public class BalanceSheetCategoryTotalDto
{
    public string Category { get; set; } = string.Empty;
    public decimal Net { get; set; }
    /// <summary>TOPLAM (net varlık) toplamına dahil mi (AccountBalance/Stock/Stone/Labor/Bullion → true).</summary>
    public bool CountsInTotal { get; set; }
}

/// <summary>
/// Bilanço sonucu: kategori×birim detay + kategori toplamları + TOPLAM (net varlık), base biriminde.
/// </summary>
public class BalanceSheetReportResultDto
{
    public BalanceSheetScope Scope { get; set; }
    public Guid BaseUnitId { get; set; }
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public DateTime AsOf { get; set; }

    public List<BalanceSheetDetailRowDto> Rows { get; set; } = new();
    public List<BalanceSheetCategoryTotalDto> CategoryTotals { get; set; } = new();

    /// <summary>TOPLAM net varlık = TOPLAM'a giren kategorilerin Net toplamı, base biriminde.</summary>
    public decimal Total { get; set; }
}
