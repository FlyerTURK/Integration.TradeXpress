namespace Integration.TradeXpress.Vaults;

/// <summary>
/// Bir şubenin (<see cref="Branches.Branch"/>) kasası — OrgScope'un en alt (yaprak) seviyesi. Tek
/// parent (<see cref="BranchId"/>, id-only referans; nav YOK). Şube oluşturulurken otomatik bir
/// <see cref="IsDefault"/> (varsayılan) kasa açılır; bir şubenin son kasası silinemez (en az 1
/// child kuralı). Per-tenant (IMultiTenant).
/// </summary>
public class Vault : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Üst şube — id-only referans (nav YOK).</summary>
    public virtual Guid BranchId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    /// <summary>Şubenin varsayılan kasası mı (otomatik açılan/birincil).</summary>
    public virtual bool IsDefault { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }
    public virtual string? Description { get; protected set; }

    protected Vault() { }

    public Vault(
        Guid branchId,
        string code,
        string name,
        bool isDefault = false,
        int displayOrder = 0)
    {
        SetBranch(branchId);
        SetCode(code);
        SetName(name);
        IsDefault = isDefault;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public virtual void SetCode(string code)
    {
        // NormalizeCode: Trim + çoklu boşluk→tek + boşluk→'_' + UPPER, ardından zorunlu/min/max doğrulaması.
        // Elle .ToUpperInvariant() gerekmez (NormalizeCode zaten UPPER yapar).
        Code = StringFieldGuard.NormalizeCode(
            code,
            nameof(Code),
            EntityFieldConsts.CodeMinLength,
            VaultConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        // NormalizeName: Trim + çoklu boşluk→tek + TitleCase, ardından zorunlu/min/max doğrulaması.
        Name = StringFieldGuard.NormalizeName(
            name,
            nameof(Name),
            EntityFieldConsts.NameMinLength,
            VaultConsts.NameMaxLength);
    }

    public virtual void SetBranch(Guid branchId)
    {
        if (branchId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Vault:BranchRequired");
        }

        BranchId = branchId;
    }

    public virtual void SetDescription(string? description)
    {
        // Opsiyonel alan: yalnız üst sınır (min yok — mevcut davranış korunur). Aşılırsa tipli Framework exception'ı.
        if (description is { Length: > VaultConsts.DescriptionMaxLength })
        {
            throw new TooLongPropertyException(nameof(Description), VaultConsts.DescriptionMaxLength);
        }

        Description = description;
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetAsDefault(bool isDefault)
    {
        IsDefault = isDefault;
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    public override string ToString()
    {
        return Code;
    }
}
