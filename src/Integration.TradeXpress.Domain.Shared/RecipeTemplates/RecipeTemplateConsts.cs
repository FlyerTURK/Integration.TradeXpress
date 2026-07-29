namespace Integration.TradeXpress.RecipeTemplates;

/// <summary>
/// Reçete şablonu ("orta reçete") alan sınırları.
///
/// <para><b>KOD ALANI YOK</b> — <c>ProductCategory</c> ile aynı gerekçe (2026-07-27 Hakan): şablon bir katalog
/// düğümüdür, kimliği ADIDIR; kod istemek her kayıtta gereksiz sürtünme yaratır. Benzersizlik şirket başına
/// AD üzerindedir.</para>
/// </summary>
public static class RecipeTemplateConsts
{
    public const int NameMaxLength = 128;
    public const int DescriptionMaxLength = 512;

    /// <summary>Şablon satırının açıklaması — reçete satırıyla AYNI sınır (uygulanınca birebir kopyalanır).</summary>
    public const int LineDescriptionMaxLength = 512;
}
