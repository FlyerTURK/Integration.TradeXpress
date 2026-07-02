using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Bilanço DRILL — bir kategori×birim değerinin oluştuğu HAREKETLER'i ister (çift-tık popup).
/// Kapsam pozisyon/bilanço ile aynı (sızıntı önlemi): Scope + BranchId; şirket sunucuda ICurrentCompany'den zorlanır.
/// </summary>
public class BalanceSheetMovementRequestDto
{
    public string Category { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public BalanceSheetScope Scope { get; set; }
    public Guid? BranchId { get; set; }
    public DateTime AsOf { get; set; }
}

/// <summary>Tek hareket satırı — Kod (belge no) + Bakiye (firma-perspektifi, görünen değerle aynı işaret). Tarih ek bilgi.</summary>
public class BalanceSheetMovementDto
{
    public string Code { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Drill sonucu — hareketler + birim kodu (başlık) + desteklenip desteklenmediği (kategori henüz yoksa false).</summary>
public class BalanceSheetMovementResultDto
{
    public string UnitCode { get; set; } = string.Empty;
    public bool Supported { get; set; }
    public List<BalanceSheetMovementDto> Movements { get; set; } = new();
}
