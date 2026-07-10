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
