using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// ÇEKİRDEK ürün kategorisi — şirkete ait (company-owned) kendi taksonomimiz. Pazaryeri kategorilerinden
/// (<c>N11Category</c>/<c>TrendyolCategory</c>) BAĞIMSIZDIR: onlar kanaldan senkronlanan dış ağaçlardır, bu ise
/// bizim kataloğumuzdur ve ileride her kanalın kategorisine EŞLEŞTİRİLİR.
///
/// <para><b>Neden var (2026-07-27 Hakan vizyonu):</b> ürün bir kez çekirdek kategoriye bağlanınca (a) her satış
/// kanalında kategori ayrı ayrı seçilmez, (b) kanal nitelikleri elle doldurulmaz, (c) kanalın kategori komisyonu
/// otomatik çözülüp reçeteye brüt maliyet olarak girer. Bugün bu üçü her kanalda tek tek yapılıyor.</para>
///
/// <para><b>Ağaç:</b> <see cref="ParentId"/> ile SERBEST derinlikli (tavan YOK — 2026-07-27 Hakan kararı),
/// id-only self-referans — navigation YOK. <c>null</c> ebeveyn = kök (ana kategori). Döngü guard'ı aggregate
/// DIŞINDA (<see cref="ProductCategoryTreeManager"/>) çözülür: entity yalnız kendi bildiğini doğrular (kendi
/// kendinin ebeveyni olamaz); ata zincirini görmek repository ister.</para>
///
/// <para><b>Kanal eşleştirmesi YAPRAK-ZORUNLU DEĞİLDİR</b> (2026-07-27 Hakan): bizim bir ARA kategorimiz, satış
/// kanalının FİNAL (yaprak) kategorisine denk gelebilir. Eşleştirmeyi kuran kod "yalnız yapraklar eşleşir"
/// varsayımı yapmamalıdır.</para>
///
/// <para><b>Kalıtım:</b> alt kategori üst kategorilerin niteliklerini ve değerlerini DEVRALIR; birleştirme
/// kuralı ve gerekçesi <see cref="ProductCategoryTreeManager.MergeAttributes"/>'ta.</para>
///
/// <para><b>Nitelikler AYRI ENTITY</b> (owned JSON DEĞİL — <c>VariantTemplate</c>'ten ayrıldığımız nokta):
/// kanal eşleştirmesi "bu nitelik N11'de şu niteliğe karşılık gelir" diyebilmek için KALICI kimlik ister.
/// JSON'da kalıcı Id olmadığından bir grubun adı değişince tüm eşleştirmeler sessizce kayardı.</para>
/// </summary>
public class ProductCategory : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected ProductCategory()
    {
    }

    public ProductCategory(Guid companyId, string name, Guid? parentId = null, int displayOrder = 0)
    {
        SetCompany(companyId);
        SetName(name);
        ParentId = NormalizeParent(parentId);
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — id-only referans (company-owned; oluşturmadan sonra değişmez).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Üst kategori — id-only, navigation YOK; <c>null</c> = kök. Ağacın bütünlüğü (döngü, derinlik,
    /// aynı şirket) manager'da doğrulanır; entity tek başına ata zincirini göremez.</summary>
    public virtual Guid? ParentId { get; protected set; }

    /// <summary>Kategori adı — kimliğin kendisi (KOD YOK; gerekçe <see cref="ProductCategoryConsts"/>'ta).
    /// Benzersizlik KARDEŞ düzeyindedir: aynı üst altında aynı ad iki kez olamaz.</summary>
    public virtual string Name { get; protected set; } = null!;

    public virtual string? Description { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }

    /// <summary>Kategorinin nitelikleri (ayrı tablo; aggregate içi koleksiyon).</summary>
    public virtual List<ProductCategoryAttribute> Attributes { get; protected set; } = new();

    #endregion

    #region Methods

    /// <summary>Kategori adı — <b>yol ayracı karakterleri YASAK</b>.
    ///
    /// <para><b>Neden:</b> kategori yolu düz metin olarak kuruluyor (<c>"Takı › Yüzük › Alyans"</c>). Ada ayraç
    /// karakteri girerse yol okunamaz hâle gelir ve tek bir kategori iki seviye gibi görünür — üstelik yol
    /// hesaplanmış bir alan olduğu için geri ayrıştırma da mümkün değil. Hem gerçek ayraç <c>›</c> hem de
    /// gözle ondan ayırt edilemeyen ASCII <c>&gt;</c> engellenir (2026-08-04 Hakan).</para></summary>
    public virtual void SetName(string name)
    {
        var normalized = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, ProductCategoryConsts.NameMaxLength);

        if (normalized.IndexOfAny(ProductCategoryConsts.ForbiddenNameCharacters) >= 0)
        {
            throw new BusinessException("TradeXpress:ProductCategory:NameHasPathSeparator")
                .WithData("Name", normalized);
        }

        Name = normalized;
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, ProductCategoryConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    /// <summary>Üst kategoriyi değiştirir. Entity YALNIZ kendi bildiğini doğrular: bir kategori kendi
    /// ebeveyni olamaz. Ata zincirinde döngü ve derinlik tavanı repository gerektirdiğinden manager'da
    /// (<see cref="ProductCategoryTreeManager"/>) doğrulanır — burada sessizce geçmek yerine orada fail-fast.</summary>
    public virtual void SetParent(Guid? parentId)
    {
        var normalized = NormalizeParent(parentId);

        if (normalized is { } value && value == Id)
        {
            throw new BusinessException("TradeXpress:ProductCategory:CannotBeOwnParent");
        }

        ParentId = normalized;
    }

    /// <summary>Yeni nitelik ekler ve onu döndürür (çağıran değerlerini doldurabilsin).</summary>
    public virtual ProductCategoryAttribute AddAttribute(
        string name,
        ProductCategoryAttributeKind kind = ProductCategoryAttributeKind.Specification,
        int displayOrder = 0)
    {
        var attribute = new ProductCategoryAttribute(Id, name, kind, displayOrder);
        Attributes.Add(attribute);
        return attribute;
    }

    /// <summary>Var olan niteliği kimliğiyle bulur — <c>null</c> ise gönderilen id bu kategoriye ait değildir.</summary>
    public virtual ProductCategoryAttribute? FindAttribute(Guid attributeId)
    {
        return Attributes.FirstOrDefault(a => a.Id == attributeId);
    }

    /// <summary>
    /// Verilen kimlik kümesi DIŞINDA kalan nitelikleri kaldırır (koleksiyondan düşen satırı EF orphan olarak siler).
    ///
    /// <para><b>Neden "hepsini yeniden yarat" değil:</b> nitelik kalıcı kimliğiyle pazaryeri niteliğine
    /// eşleştirilecek. Güncellemede listeyi baştan kurmak her kaydetmede yeni Id üretir ve tüm eşleştirmeleri
    /// sessizce koparırdı — bu yüzden güncelleme MERGE'dir, replace değil.</para>
    /// </summary>
    public virtual void RemoveAttributesExcept(IReadOnlyCollection<Guid> keepAttributeIds)
    {
        Attributes.RemoveAll(a => a.Id != Guid.Empty && !keepAttributeIds.Contains(a.Id));
    }

    public override string ToString()
    {
        return Name;
    }

    // Boş Guid "seçilmedi" demektir → kök. Aksi hâlde var olmayan bir ebeveyne asılı öksüz kayıt doğardı.
    private static Guid? NormalizeParent(Guid? parentId)
    {
        return parentId is { } value && value != Guid.Empty ? value : null;
    }

    // Company set-once (oluşturmada) → public mutator YOK; yalnız ctor.
    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    #endregion
}
