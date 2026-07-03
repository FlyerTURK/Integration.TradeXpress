namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>Bilanço snapshot alan sınırları (EF config + entity ortak referansı).</summary>
public static class BalanceSheetSnapshotConsts
{
    /// <summary>Kategori anahtarı (<c>BalanceSheetCategory</c> sabitleri; en uzun "AccountBalance").</summary>
    public const int CategoryMaxLength = 64;

    /// <summary>Base para birimi kodu (CurrencyUnit.Code ile aynı sınıf; kısa kod).</summary>
    public const int BaseCurrencyCodeMaxLength = 16;
}
