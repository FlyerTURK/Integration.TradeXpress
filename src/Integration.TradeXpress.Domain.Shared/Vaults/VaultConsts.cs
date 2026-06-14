namespace Integration.TradeXpress.Vaults;

/// <summary>Vault (kasa) alan sınırları.</summary>
public static class VaultConsts
{
    public const int CodeMaxLength = 32;
    public const int NameMaxLength = 128;
    public const int DescriptionMaxLength = 512;

    public const string DefaultCode = "KASA";
    /// <summary>Şube oluşturulurken otomatik açılan varsayılan kasanın adı.</summary>
    public const string DefaultName = "Ana Kasa";
}
