namespace Integration.TradeXpress.Branches;

/// <summary>Branch (şube) alan sınırları.</summary>
public static class BranchConsts
{
    public const int NameMaxLength = 128;
    public const int DescriptionMaxLength = 512;

    /// <summary>Şirket oluşturulurken otomatik açılan merkez (HQ) şubenin varsayılan adı.</summary>
    public const string DefaultHeadquartersName = "Merkez Şube";
}
