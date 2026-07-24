using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil grubu satırı — grubun SIRALI emtia listesinin bir elemanı. AYRI aggregate
/// (<c>ProductAttributeValue</c> deseni: id-only referans, nav YOK); gruba
/// <see cref="SubstitutionGroupId"/> ile bağlanır (set-once).
/// <para><b>MetalId XOR MetalGroupId</b> (konsept "maden grupları" genişleme notu): İLK FAZDA yalnız
/// <see cref="MetalId"/> kullanılır; <see cref="MetalGroupId"/> kolonu REZERVE (ileride maden grubu
/// referansı — gruptaki tüm madenler listeye canlı çözülür; solver düz emtia listesi aldığından motor
/// etkilenmez). En az biri dolu invariant'ı entity'de (fail-fast).</para>
/// <para><see cref="DisplayOrder"/> = TÜKETİM ÖNCELİĞİ: liste sırası kullanıcı-kontrollü; üsttekiler
/// önce tüketilir, zor bulunan/korunacak emtia listenin sonuna konur.</para>
/// </summary>
public class SubstitutionGroupItem : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SubstitutionGroupItem() { }

    public SubstitutionGroupItem(
        Guid companyId,
        Guid substitutionGroupId,
        Guid? metalId,
        Guid? metalGroupId = null,
        int displayOrder = 0)
    {
        SetCompany(companyId);
        SetGroup(substitutionGroupId);
        SetTarget(metalId, metalGroupId);
        DisplayOrder = displayOrder;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — denormalize güvenlik sınırı. Oluşturmadan sonra değişmez.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip muadil grubu — id-only referans. Oluşturmadan sonra değişmez (set-once).</summary>
    public virtual Guid SubstitutionGroupId { get; protected set; }

    /// <summary>Emtia referansı — Metal (adet-hesaplı + StableQuantity). İlk fazda TEK aktif hedef.</summary>
    public virtual Guid? MetalId { get; protected set; }

    /// <summary>REZERVE — ileride maden grubu referansı (GR995 vb.); şimdilik daima null.</summary>
    public virtual Guid? MetalGroupId { get; protected set; }

    /// <summary>Tüketim önceliği sırası — küçük önce tüketilir (kullanıcı-kontrollü liste sırası).</summary>
    public virtual int DisplayOrder { get; protected set; }

    /// <summary>
    /// Muadil değerlendirmesine DAHİL varyantlar (OPT-IN; EF primitive-collection → JSON kolonu).
    /// <para><b>Boş liste = yalnız ANA varyant dahil</b> (statüko): mevcut gruplar dokunulmadan aynen çalışır.
    /// Kullanıcı ağaçta yalnız ana varyantı bırakırsa da BOŞ listeye normalize edilir (boş=ana değişmezinin
    /// TEK temsili; normalizasyon yazma sınırında — AppService). Yeni doğan varyant OTOMATİK DAHİL DEĞİLDİR:
    /// opt-in'in amacı, maliyeti henüz oturmamış yeni varyantın sessizce muadile karışmaması.</para>
    /// <para><b>İş gerekçesi:</b> metal varyantlarının işçilik/maliyeti artık ayrışıyor (yeni tarihli çeyrek
    /// toptancıdan İŞÇİLİKLİ; eski tarihli perakendeden işçiliksiz hurda) → muadillik varyant düzeyinde
    /// seçilebilir olmalı. Bu dilimde yalnız SAKLANIR; çözücü entegrasyonu sonraki dilimin işi.</para>
    /// </summary>
    public virtual List<Guid> IncludedVariantIds { get; protected set; } = new();

    #endregion

    #region Methods

    /// <summary>Emtia hedefi — MetalId ya da MetalGroupId'den EN AZ BİRİ dolu olmalı (fail-fast).</summary>
    public virtual void SetTarget(Guid? metalId, Guid? metalGroupId)
    {
        var metal = metalId == Guid.Empty ? null : metalId;
        var metalGroup = metalGroupId == Guid.Empty ? null : metalGroupId;

        if (metal is null && metalGroup is null)
        {
            throw new BusinessException("TradeXpress:Substitution:ItemTargetRequired");
        }

        MetalId = metal;
        MetalGroupId = metalGroup;
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    /// <summary>Dahil varyant kümesini yazar — distinct + boş-Guid ayıklanır (sıra korunur); null → BOŞ liste
    /// (boş = yalnız ana varyant, statüko). "{yalnız ana}" → boş normalizasyonu ana-varyant bilgisini bilen
    /// yazma sınırında (AppService) yapılır — entity varyant kataloğunu bilmez.</summary>
    public virtual void SetIncludedVariants(IEnumerable<Guid>? variantIds)
    {
        var normalized = (variantIds ?? Enumerable.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        IncludedVariantIds = normalized;
    }

    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    private void SetGroup(Guid substitutionGroupId)
    {
        if (substitutionGroupId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SubstitutionGroupId));
        }

        SubstitutionGroupId = substitutionGroupId;
    }

    public override string ToString()
    {
        return $"SubstitutionGroupItem #{DisplayOrder} → {(MetalId.HasValue ? $"Metal:{MetalId}" : $"MetalGroup:{MetalGroupId}")}";
    }

    #endregion
}
