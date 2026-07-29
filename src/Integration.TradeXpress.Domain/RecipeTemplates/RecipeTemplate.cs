using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.RecipeTemplates;

/// <summary>
/// REÇETE ŞABLONU ("orta reçete") — ürüne uygulanan hizmet/yarı mamul demeti. Company-owned.
///
/// <para><b>Neden var (2026-07-27 Hakan vizyonu, birebir):</b> "Reçetelerin emtialarını muadillikten otomatik
/// çözebilmiş durumdayız… geriye kalıyor paketleme, kargo ve sigortalama, yani ORTA REÇETELER. Onun için bir
/// reçete şablonu oluşturalım ve o şablona belirli hizmetler, yarı mamuller gibi şeyler ekleyebilelim. Ürünlere
/// şablonu yansıtırken zaten ana emtialar devir alınacak; sonrasında şablon bu devir aldığı emtiaların ÜZERİNE
/// işleyecek."</para>
///
/// <para><b>Uygulama semantiği — EKLEMELİ, ezici değil:</b> şablon ürüne uygulandığında muadillikten gelen
/// emtia satırlarına DOKUNMAZ; kendi satırlarını onların ARDINA ekler (<c>RecipeLineOrigin.Template</c> ile
/// işaretli). Sıra kritiktir: hizmet satırları "üstümdeki her şeyin toplamı" üzerinden hesaplar, dolayısıyla
/// emtia tabanı üstte olmalıdır. Yeniden uygulandığında yalnız KENDİ işaretli satırları tazelenir — kullanıcının
/// elle eklediği satırlar korunur.</para>
///
/// <para><b>Ürünle kalıcı bağ KURMAZ</b> (VariantTemplate deseni): şablon bir KAYNAKTIR. Bağ kurulsaydı şablonda
/// yapılan bir değişiklik ona bağlı yüzlerce ürünün maliyetini habersiz değiştirirdi; uygulama açık bir istektir.</para>
/// </summary>
public class RecipeTemplate : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected RecipeTemplate()
    {
    }

    public RecipeTemplate(Guid companyId, string name, int displayOrder = 0)
    {
        SetCompany(companyId);
        SetName(name);
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — set-once (company-owned).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Şablon adı — kimliğin kendisi (KOD YOK; gerekçe <see cref="RecipeTemplateConsts"/>).</summary>
    public virtual string Name { get; protected set; } = null!;

    public virtual string? Description { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }

    /// <summary>Şablon satırları (ayrı tablo; aggregate içi koleksiyon). Sıra <see cref="RecipeTemplateLine.LineOrder"/>.</summary>
    public virtual List<RecipeTemplateLine> Lines { get; protected set; } = new();

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, RecipeTemplateConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, RecipeTemplateConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    /// <summary>Yeni satır ekler ve döndürür (çağıran tür-özel alanları doldurur).</summary>
    public virtual RecipeTemplateLine AddLine(RecipeComponentType componentType, int lineOrder)
    {
        var line = new RecipeTemplateLine(Id, componentType, lineOrder);
        Lines.Add(line);
        return line;
    }

    public virtual RecipeTemplateLine? FindLine(Guid lineId)
    {
        return Lines.FirstOrDefault(l => l.Id == lineId);
    }

    /// <summary>Tek satırı kaldırır — tür değişiminde kullanılır (bileşen türü satırın kimliğinin parçasıdır;
    /// yerinde değiştirmek karşı türün artık alanlarını taşıyan melez bir satır bırakırdı).</summary>
    public virtual void RemoveLine(RecipeTemplateLine line)
    {
        Lines.Remove(line);
    }

    /// <summary>Verilen kimlik kümesi dışındaki satırları kaldırır — kategori niteliklerindeki MERGE mantığının
    /// aynısı: satır kimlikleri korunur ki düzenleme geçmişi ve ileride kurulacak referanslar kopmasın.</summary>
    public virtual void RemoveLinesExcept(IReadOnlyCollection<Guid> keepLineIds)
    {
        Lines.RemoveAll(l => l.Id != Guid.Empty && !keepLineIds.Contains(l.Id));
    }

    public override string ToString()
    {
        return Name;
    }

    // Şirket set-once → public mutator YOK; yalnız ctor.
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
