namespace Integration.TradeXpress.Accounts;

/// <summary>Account / SubAccount alanları için merkezî sınırlar.</summary>
public static class AccountConsts
{
    public const int CodeMaxLength        = 32;
    public const int NameMaxLength        = 192;
    public const int DescriptionMaxLength = 512;

    /// <summary>Yeni cari hesapla birlikte otomatik açılan varsayılan alt hesabın kodu/adı (en az 1 alt hesap kuralı).</summary>
    public const string DefaultSubAccountCode = "ANAHESAP";
    public const string DefaultSubAccountName = "Ana Hesap";
}
