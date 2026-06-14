using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Branches;

/// <summary>
/// Şirketin (<see cref="Companies.Company"/>) bir şubesi — OrgScope'un orta seviyesi. Tek parent
/// (<see cref="CompanyId"/>, id-only referans; nav YOK, aggregate sınırı). Her şirket en az bir
/// <see cref="IsHeadquarters"/> (merkez) şubeyle doğar; şube oluşturulurken otomatik bir
/// <see cref="Vaults.Vault"/> (kasa) açılır. Per-tenant (IMultiTenant).
///
/// <para>HQ devri: bir şubeyi HQ yapmak, şirketin önceki HQ şubesini düşürür (AppService doğrular,
/// şirket başına tek HQ). HQ şube, HQ başka bir şubeye devredilmedikçe silinemez; ayrıca şirketin
/// son şubesi silinemez (en az 1 child kuralı).</para>
/// </summary>
public class Branch : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Üst şirket — id-only referans (nav YOK).</summary>
    public virtual Guid CompanyId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    /// <summary>Şirketin merkez (HQ) şubesi mi. Şirket başına tek HQ (AppService doğrular).</summary>
    public virtual bool IsHeadquarters { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }
    public virtual string? Description { get; protected set; }

    protected Branch() { }

    public Branch(
        Guid id,
        Guid companyId,
        string code,
        string name,
        bool isHeadquarters = false,
        int displayOrder = 0,
        Guid? tenantId = null)
        : base(id)
    {
        SetCompany(companyId);
        SetCode(code);
        SetName(name);
        IsHeadquarters = isHeadquarters;
        DisplayOrder = displayOrder;
        TenantId = tenantId;
        IsActive = true;
    }

    public virtual void SetCode(string code)
        => Code = Check.NotNullOrWhiteSpace(code, nameof(code), BranchConsts.CodeMaxLength).ToUpperInvariant();

    public virtual void SetName(string name)
        => Name = Check.NotNullOrWhiteSpace(name, nameof(name), BranchConsts.NameMaxLength);

    public virtual void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company is required.", nameof(companyId));
        CompanyId = companyId;
    }

    public virtual void SetDescription(string? description)
    {
        if (description is { Length: > BranchConsts.DescriptionMaxLength })
            throw new ArgumentException(
                $"Description length must be at most {BranchConsts.DescriptionMaxLength}.", nameof(description));
        Description = description;
    }

    public virtual void Activate() => IsActive = true;
    public virtual void Deactivate() => IsActive = false;
    public virtual void SetAsHeadquarters(bool isHeadquarters) => IsHeadquarters = isHeadquarters;
    public virtual void SetDisplayOrder(int order) => DisplayOrder = order;
}
