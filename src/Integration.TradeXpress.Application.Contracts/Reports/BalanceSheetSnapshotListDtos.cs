using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Kaydedilmiş bilanço snapshot'larının GEÇMİŞ listesi isteği. Kapsam (Branch/Company) + opsiyonel şube.
/// CompanyId client'tan GÖNDERİLMEZ — sunucu <c>ICurrentCompany</c>'den zorlar (ComputeAsync/SaveAsync ile aynı).
/// </summary>
public class BalanceSheetSnapshotListRequestDto
{
    public BalanceSheetScope Scope { get; set; } = BalanceSheetScope.Branch;
    public Guid? BranchId { get; set; }
}

/// <summary>
/// Bir kayıtlı bilanço günü (AsOfDate) — kategorileri PIVOT'lanmış tek satır + running türetimler
/// (DEVIR/KARZARAR/MASRAF/GUNLUK). ERPPRO <c>BilancoListesi</c> satırı paritesi. KURFARKI/RevCostDate bu fazda YOK.
/// </summary>
public class BalanceSheetSnapshotRowDto
{
    public DateTime AsOfDate { get; set; }
    public BalanceSheetScope Scope { get; set; }

    /// <summary>Şube kodu (Company scope'ta boş/konsolide).</summary>
    public string? BranchCode { get; set; }

    /// <summary>Kategori anahtarı (<see cref="BalanceSheetCategory"/>) → o günkü Net toplamı, base biriminde.</summary>
    public Dictionary<string, decimal> CategoryNets { get; set; } = new();

    /// <summary>TOPLAM (net varlık) = TOPLAM'a giren kategorilerin Net toplamı (Expense/Income hariç).</summary>
    public decimal Total { get; set; }

    /// <summary>DEVİR = önceki günün TOPLAM'ı (ilk gün 0).</summary>
    public decimal Devir { get; set; }

    /// <summary>KAR/ZARAR = TOPLAM − DEVİR (dönemler arası net varlık değişimi).</summary>
    public decimal KarZarar { get; set; }

    /// <summary>MASRAF = Expense + Income Net toplamı (P&L; TOPLAM dışı).</summary>
    public decimal Masraf { get; set; }

    /// <summary>GÜNLÜK = MASRAF delta (önceki güne göre; ilk gün MASRAF'ın kendisi).</summary>
    public decimal Gunluk { get; set; }

    /// <summary>KUR FARKI = gün-aşırı yeniden değerleme kar/zararı (ERPPRO GetKurFarki): önceki snapshot satırının
    /// pozisyonu BU günün kuruyla yeniden değerlenip donuk Net'ten çıkarılır (birim bazında Σ). TOPLAM DIŞI; ilk gün 0.</summary>
    public decimal KurFarki { get; set; }

    /// <summary>Bilanço (base) birimi kodu — o snapshot'ta dondurulmuş.</summary>
    public string BaseCurrencyCode { get; set; } = string.Empty;
}

/// <summary>Kayıtlı bilanço geçmişi sonucu: PIVOT satır listesi (tarih ARTAN) + görünen kategori anahtarları.</summary>
public class BalanceSheetSnapshotListDto
{
    /// <summary>Snapshot'larda görünen kategori anahtarları (kolon başlıkları için; TOPLAM sırasında sabit).</summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>Tarih ARTAN sıralı PIVOT satırları (her AsOfDate = 1 satır).</summary>
    public List<BalanceSheetSnapshotRowDto> Rows { get; set; } = new();
}
