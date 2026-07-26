using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// N11 kargo firması — <b>HOST-GLOBAL</b> referans (IMultiTenant DEĞİL; TenantId yok → tüm tenant'lar paylaşır).
/// N11 ShipmentCompanyService.GetShipmentCompanies'ten sync'lenir (~68 firma; SOAP — REST'te yok). Düz liste:
/// ExternalId + Name + ShortName. Aktif olabildiğinden periyodik re-sync (ekle/güncelle/sil).
/// <para><b>Çekirdeği BİLMEZ</b> (2026-07-26): eskiden <c>CoreCarrierId</c> ile tekil köprü taşırdı; çekirdek
/// <c>TrCarrier</c> company-owned olunca host-global tek satır N şirketin carrier'ından hangisini göstereceğini
/// adresleyemez hâle geldi. Köprü sahipli tarafa taşındı (<c>TrCarrier.N11ShipmentCompanyId</c>) — ayna artık
/// tenant/company dünyasından tamamen habersiz, katman yönü temiz.</para>
/// </summary>
public class N11ShipmentCompany : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected N11ShipmentCompany()
    {
    }

    public N11ShipmentCompany(string externalId, string name, string? shortName)
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

    /// <summary>Kısa kod (ör. ARAS, YK) — <b>OPSİYONEL</b>: N11 bazı firmaları kısa-kodSUZ döndürür
    /// (DHL/Asil/Fillo Kargo). Ayna entity N11'in wire gerçeğini olduğu gibi taşır; kod TÜRETME
    /// (boşsa Name'den) çekirdek tarafın (<c>TrCarrierSeeder</c>) işidir.</summary>
    public string? ShortName { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, N11ShipmentConsts.NameMaxLength);
    }

    /// <summary>Kısa kodu set eder — BOŞ KABUL EDİLİR (2026-07-26 canlı bulgu). Eskiden zorunluydu ve N11'in
    /// kısa-kodsuz döndürdüğü İLK firmada (DHL/Asil/Fillo) RequiredPropertyException fırlatıp SYNC'İN TAMAMINI
    /// düşürüyordu → tabloda 0 firma, dolayısıyla çekirdek kargo kataloğu da hiç kurulamıyordu. Worker hatayı
    /// "kimlik/ağ?" diye loglayıp yuttuğundan sorun aylarca görünmezdi.</summary>
    public virtual void SetShortName(string? shortName)
    {
        ShortName = StringFieldGuard.EnsureOptionalText(shortName, nameof(ShortName), 1, N11ShipmentConsts.ShortNameMaxLength);
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}
