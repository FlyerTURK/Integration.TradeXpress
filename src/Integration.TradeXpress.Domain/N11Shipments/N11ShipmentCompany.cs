using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// N11 kargo firması — <b>HOST-GLOBAL</b> referans (IMultiTenant DEĞİL; TenantId yok → tüm tenant'lar paylaşır).
/// N11 ShipmentCompanyService.GetShipmentCompanies'ten sync'lenir (~68 firma; SOAP — REST'te yok). Düz liste:
/// ExternalId + Name + ShortName. Aktif olabildiğinden periyodik re-sync (ekle/güncelle/sil).
/// </summary>
public class N11ShipmentCompany : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected N11ShipmentCompany()
    {
    }

    public N11ShipmentCompany(string externalId, string name, string shortName)
    {
        ExternalId = StringFieldGuard.EnsureRequiredText(externalId, nameof(ExternalId), 1, N11ShipmentConsts.ExternalIdMaxLength);
        SetName(name);
        SetShortName(shortName);
    }

    #endregion

    #region Properties

    /// <summary>N11 kargo firması id'si (ör. 345). Global benzersiz.</summary>
    public string ExternalId { get; protected set; } = string.Empty;

    public string Name { get; protected set; } = string.Empty;

    /// <summary>Kısa kod (ör. ARAS, YK).</summary>
    public string ShortName { get; protected set; } = string.Empty;

    /// <summary>Çekirdek kargo firmasına gevşek köprü — eşlenen <see cref="Shipments.Carrier"/> id'si (nav YOK;
    /// N11 çekirdeği BİLMEZ). CarrierSeeder doldurur; null = henüz eşlenmedi.</summary>
    public Guid? CoreCarrierId { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, N11ShipmentConsts.NameMaxLength);
    }

    public virtual void SetShortName(string shortName)
    {
        ShortName = StringFieldGuard.EnsureRequiredText(shortName, nameof(ShortName), 1, N11ShipmentConsts.ShortNameMaxLength);
    }

    /// <summary>Çekirdek kargo firması köprüsünü set eder (boş Guid → null). CarrierSeeder çağırır.</summary>
    public virtual void SetCoreCarrier(Guid? carrierId)
    {
        CoreCarrierId = carrierId == Guid.Empty ? null : carrierId;
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}
