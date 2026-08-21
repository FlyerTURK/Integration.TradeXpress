using System;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Domain.Entities.Auditing;

namespace Integration.TradeXpress.Metals;

/// <summary>
/// Bir varyantın METAL-ÖZEL işçilik detayı — jenerik <c>EntityVariant</c>'ın Metal uzantısı (1:1, <see cref="EntityVariantId"/>
/// set-once). İşçilik (Labor) tanımları VARYANT seviyesindedir.
/// Company-scoped (varyanttan denormalize) + per-tenant. Jenerik <c>EntityVariant</c> bu uzantıyı BİLMEZ.
/// </summary>
public class MetalVariantDetail : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected MetalVariantDetail()
    {
    }

    public MetalVariantDetail(Guid? companyId, Guid entityVariantId)
    {
        CompanyId = companyId;
        SetVariant(entityVariantId);
        LaborType = MetalLaborType.Amount; // default
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — varyanttan denormalize (null = tenant-geneli). Değişmez.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Detaylandırdığı jenerik varyant — id-only, set-once (1:1).</summary>
    public virtual Guid EntityVariantId { get; protected set; }

    // ── İşçilik ──
    public virtual MetalLaborType LaborType { get; protected set; }
    public virtual bool LaborTypeChange { get; protected set; }
    public virtual decimal EntryLabor { get; protected set; }
    public virtual Guid? EntryLaborUnitId { get; protected set; }
    public virtual bool EntryLaborChange { get; protected set; }
    public virtual decimal ExitLabor { get; protected set; }
    public virtual Guid? ExitLaborUnitId { get; protected set; }
    public virtual bool ExitLaborChange { get; protected set; }
    public virtual Guid? CostUnitId { get; protected set; }

    #endregion

    #region Methods

    private void SetVariant(Guid entityVariantId)
    {
        if (entityVariantId == Guid.Empty)
        {
            throw new Integration.Framework.RequiredPropertyException(nameof(EntityVariantId));
        }

        EntityVariantId = entityVariantId;
    }

    public virtual void SetLabor(
        MetalLaborType laborType, bool laborTypeChange,
        decimal entryLabor, Guid? entryLaborUnitId, bool entryLaborChange,
        decimal exitLabor, Guid? exitLaborUnitId, bool exitLaborChange,
        Guid? costUnitId)
    {
        LaborType        = laborType;
        LaborTypeChange  = laborTypeChange;
        EntryLabor       = entryLabor;
        EntryLaborUnitId = entryLaborUnitId;
        EntryLaborChange = entryLaborChange;
        ExitLabor        = exitLabor;
        ExitLaborUnitId  = exitLaborUnitId;
        ExitLaborChange  = exitLaborChange;
        CostUnitId       = costUnitId;
    }

    #endregion
}
