namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Kalıtım çözüldükten sonra bir kategorinin ETKİN niteliği — entity DEĞİL, hesap sonucudur (DB'ye yazılmaz).
/// Her satır nereden geldiğini taşır (<see cref="SourceCategoryId"/> + <see cref="IsInherited"/>): UI devralınanı
/// ayırt edip salt-okunur gösterebilsin, kanal eşleştirmesi de değeri SAHİBİ kategorideki kalıcı kimliğiyle bağlasın.
/// </summary>
public class ProductCategoryEffectiveAttribute
{
    #region Constructors

    public ProductCategoryEffectiveAttribute(
        ProductCategoryAttribute attribute,
        ProductCategory source,
        bool isInherited)
    {
        AttributeId = attribute.Id;
        Name = attribute.Name;
        Kind = attribute.Kind;
        DisplayOrder = attribute.DisplayOrder;
        SourceCategoryId = source.Id;
        SourceCategoryName = source.Name;
        IsInherited = isInherited;
    }

    #endregion

    #region Properties

    /// <summary>Niteliğin kalıcı kimliği — SAHİBİ kategorideki satırın Id'si (devralınmışsa üst kategoriye aittir).</summary>
    public Guid AttributeId { get; private set; }

    public string Name { get; private set; }

    public ProductCategoryAttributeKind Kind { get; private set; }

    public int DisplayOrder { get; private set; }

    /// <summary>Niteliği (son olarak) tanımlayan kategori.</summary>
    public Guid SourceCategoryId { get; private set; }

    public string SourceCategoryName { get; private set; }

    /// <summary>Bu kategoriye ait mi (<c>false</c>) yoksa bir üstten mi geldi (<c>true</c>).</summary>
    public bool IsInherited { get; private set; }

    public List<ProductCategoryEffectiveAttributeValue> Values { get; } = new();

    #endregion

    #region Methods

    /// <summary>Aynı adlı nitelik daha alt bir seviyede yeniden tanımlandığında: cins/sıra en DAR tanımdan gelir.
    /// Kimlik de alt tanıma geçer — kategori kendi niteliğini düzenlerken üstteki satırı değil kendininkini görmeli.</summary>
    public void Redefine(ProductCategoryAttribute attribute, ProductCategory source, bool isInherited)
    {
        AttributeId = attribute.Id;
        Name = attribute.Name;
        Kind = attribute.Kind;
        DisplayOrder = attribute.DisplayOrder;
        SourceCategoryId = source.Id;
        SourceCategoryName = source.Name;
        IsInherited = isInherited;
    }

    /// <summary>Değeri ekler. Aynı metin zaten varsa (case duyarsız) TEKRAR EKLENMEZ — üstten devralınan değer
    /// altta yeniden yazıldığında liste ikizlenmesin; ilk (yani en üstteki) kaynak korunur.</summary>
    public void AddValue(ProductCategoryAttributeValue value, ProductCategory source, bool isInherited)
    {
        if (Values.Any(v => string.Equals(v.Value, value.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Values.Add(new ProductCategoryEffectiveAttributeValue(value, source, isInherited));
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}

/// <summary>Etkin nitelik değeri — kaynağıyla birlikte (devralınan değer üst kategorinin satırıdır).</summary>
public class ProductCategoryEffectiveAttributeValue
{
    #region Constructors

    public ProductCategoryEffectiveAttributeValue(
        ProductCategoryAttributeValue value,
        ProductCategory source,
        bool isInherited)
    {
        ValueId = value.Id;
        Value = value.Value;
        DisplayOrder = value.DisplayOrder;
        SourceCategoryId = source.Id;
        SourceCategoryName = source.Name;
        IsInherited = isInherited;
    }

    #endregion

    #region Properties

    /// <summary>Değerin kalıcı kimliği — kanal değer eşleştirmesi buna asılır.</summary>
    public Guid ValueId { get; private set; }

    public string Value { get; private set; }

    public int DisplayOrder { get; private set; }

    public Guid SourceCategoryId { get; private set; }

    public string SourceCategoryName { get; private set; }

    public bool IsInherited { get; private set; }

    #endregion

    #region Methods

    public override string ToString()
    {
        return Value;
    }

    #endregion
}
