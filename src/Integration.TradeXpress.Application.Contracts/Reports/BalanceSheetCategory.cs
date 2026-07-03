using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Bilanço KATEGORİ (Cinsi) anahtarları — ERPPRO <c>Bilanco.OzetBilanco</c> Cinsi'leriyle eşlenir. Her kategori
/// bir <c>IBalanceSheetCategorySource</c> ile beslenir (pluggable → yenisi eklenince rapor otomatik toplar).
/// </summary>
public static class BalanceSheetCategory
{
    public const string AccountBalance = "AccountBalance";   // ERPPRO BAKIYE — cari hesap döviz/emtia bakiyeleri (ledger)
    public const string Stock          = "Stock";            // ERPPRO STOK — nakit/emtia/maden/alaşım stokları
    public const string Labor          = "Labor";            // ERPPRO ISCILIK — YALNIZ maden işçilik maliyeti (takoz işçiliği cari/BAKİYE'de kalır, buraya girmez)
    public const string Stone          = "Stone";            // Kıymetli taşlar (Stone entity) — maliyet envanteri (ERPPRO TAS)
    public const string Bullion        = "Bullion";          // ERPPRO TAKOZ — külçe metal içeriği (HAS/GUM/PLT/PLD); işçilik YOK (cari/BAKİYE'de). BullionCategorySource ile beslenir
    public const string Jewelry        = "Jewelry";          // Kıymetli taş barındıran TAKILAR (mücevher); ERPPRO bunu yanlışlıkla "PIRLANTA" adlandırmış — maliyet envanteri
    public const string Expense        = "Expense";          // ERPPRO GIDER — P&L gider (değerlenmez, TOPLAM dışı)
    public const string Income         = "Income";           // ERPPRO GELIR — P&L gelir (değerlenmez, TOPLAM dışı)

    /// <summary>TOPLAM (SON DURUM / net varlık) toplamına girMEYEN kategoriler — yalnız gerçek P&L (gider/gelir).
    /// İŞÇİLİK TOPLAM'a GİRER (ERPPRO paritesi): işçilik = SERMAYE/VARLIK; BAKİYE'deki işçilik cari'sini offset eder
    /// (alış-anı: BAKİYE −36.13 + İŞÇİLİK +3.08 + STOK +33.05 = 0 break-even). GİRİŞ +, ÇIKIŞ −.</summary>
    private static readonly HashSet<string> ExcludedFromTotal = new(StringComparer.Ordinal)
    {
        Expense, Income,
    };

    /// <summary>TOPLAM'a dahil mi. ERPPRO <c>BilancoDetay.Total = Sum(Net)</c> ile birebir: GIDER/GELIR Net'i null
    /// (değerlenmez) → otomatik düşer; DİĞER TÜM kategoriler (Jewelry/Bullion + ileride eklenecek yeni) DAHİL.</summary>
    public static bool CountsInTotal(string category) => !ExcludedFromTotal.Contains(category);
}
