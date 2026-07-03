using System;
using Integration.TradeXpress.Bullions;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Takoz stoğu (aktif külçe) — takoz ÇIKIŞ panelinin combo kaynağı. Her satır bir GİRİŞ
/// takoz satırını (külçeyi) temsil eder; çıkış bu satıra referans verir (kısmi çıkış YOK,
/// külçe bütün çıkar). Metal alanları çıkışta SALT-OKUNUR gösterilir; kayıtta server bunları
/// giriş satırından KOPYALAR (client değerlerine güvenmez).
/// </summary>
public class BullionStockItemDto
{
    /// <summary>Külçenin geldiği GİRİŞ satırının Id'si — çıkış satırı bunu CommodityId olarak taşır.</summary>
    public Guid EntryLineId { get; set; }

    /// <summary>Külçe kodu (ör. "TK00002").</summary>
    public string Code { get; set; } = string.Empty;

    public BullionType? BullionType { get; set; }
    public bool IsReport { get; set; }
    public bool IsExtra { get; set; }
    public decimal Amount { get; set; }
    public decimal AssayAmount { get; set; }
    public decimal GoldFactor { get; set; }
    public decimal SilverFactor { get; set; }
    public decimal PlatinumFactor { get; set; }
    public decimal PalladiumFactor { get; set; }
    public string? ReportNo { get; set; }
    public Guid? AssayOfficeId { get; set; }
    public DateTime EntryDate { get; set; }

    /// <summary>Külçeyi getiren cari (giriş satırının fişindeki SubAccount).</summary>
    public Guid? SubAccountId { get; set; }

    // ── Denormalize gösterim alanları (combo kolonları) ──
    public string? AssayOfficeName { get; set; }
    public string? SubAccountDisplay { get; set; }

    /// <summary>Stokta mı (aktif çıkışı yok mu). inStock filtresi bunun üzerinden çalışır.</summary>
    public bool InStock { get; set; }

    /// <summary>Varsa son çıkış zamanı (stokta değilse dolu).</summary>
    public DateTime? ExitDate { get; set; }
}
