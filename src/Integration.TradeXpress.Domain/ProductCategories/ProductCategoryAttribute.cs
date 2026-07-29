using Integration.TradeXpress.Variants;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Kategorinin bir NİTELİĞİ ("Ayar", "Renk", "Materyal") — aggregate içi ayrı entity.
///
/// <para><b>Neden JSON değil tablo:</b> bu nitelik ileride pazaryeri niteliğine EŞLEŞTİRİLECEK ("Ayar" →
/// N11'de şu attributeId). Eşleştirme kalıcı bir kimlik ister; owned JSON'da kimlik olmadığından grubun adı
/// veya sırası değiştiğinde tüm eşleştirmeler sessizce kayardı. <c>VariantTemplate</c> JSON kullanır çünkü
/// orası self-contained bir demettir ve dışarıdan referanslanmaz — burası tam tersi.</para>
///
/// <para><see cref="Kind"/> niteliğin ürüne nasıl yansıyacağını belirler (spesifikasyon = canlı okunur,
/// varyant ekseni = ürüne eklemeli yansır); ayrımın gerekçesi enum'da yazılı.</para>
/// </summary>
public class ProductCategoryAttribute : FullAuditedEntity<Guid>
{
    #region Constructors

    protected ProductCategoryAttribute()
    {
    }

    public ProductCategoryAttribute(
        Guid categoryId,
        string name,
        ProductCategoryAttributeKind kind = ProductCategoryAttributeKind.Specification,
        int displayOrder = 0)
    {
        CategoryId = categoryId;
        SetName(name);
        Kind = kind;
        DisplayOrder = displayOrder;
    }

    #endregion

    #region Properties

    /// <summary>Sahip kategori (aggregate içi FK; navigation YOK — koleksiyon üzerinden erişilir).</summary>
    public virtual Guid CategoryId { get; protected set; }

    public virtual string Name { get; protected set; } = null!;

    /// <summary>Ürüne yansıma biçimi — spesifikasyon mu varyant ekseni mi.</summary>
    public virtual ProductCategoryAttributeKind Kind { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }

    /// <summary>Niteliğin seçilebilir değerleri ("14K", "18K"). Boş liste = serbest metin niteliği
    /// (kullanıcı ürün tarafında kendi değerini yazar) — spesifikasyonda meşru, varyant ekseninde
    /// kartezyen üretilemeyeceği için anlamsızdır.</summary>
    public virtual List<ProductCategoryAttributeValue> Values { get; protected set; } = new();

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(
            name, nameof(Name), 1, EntityVariantConsts.AttributeNameMaxLength);
    }

    public virtual void SetKind(ProductCategoryAttributeKind kind)
    {
        Kind = kind;
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    /// <summary>Yeni değer ekler ve onu döndürür.</summary>
    public virtual ProductCategoryAttributeValue AddValue(string value, int displayOrder = 0)
    {
        var item = new ProductCategoryAttributeValue(Id, value, displayOrder);
        Values.Add(item);
        return item;
    }

    /// <summary>Var olan değeri kimliğiyle bulur — <c>null</c> ise gönderilen id bu niteliğe ait değildir.</summary>
    public virtual ProductCategoryAttributeValue? FindValue(Guid valueId)
    {
        return Values.FirstOrDefault(v => v.Id == valueId);
    }

    /// <summary>Verilen kimlik kümesi dışındaki değerleri kaldırır — nitelikteki gerekçenin aynısı: değer de
    /// pazaryeri değerine kimliğiyle eşleştirileceğinden güncelleme MERGE'dir, replace değil.</summary>
    public virtual void RemoveValuesExcept(IReadOnlyCollection<Guid> keepValueIds)
    {
        Values.RemoveAll(v => v.Id != Guid.Empty && !keepValueIds.Contains(v.Id));
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}
