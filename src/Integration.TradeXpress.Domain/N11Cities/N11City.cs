using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Integration.TradeXpress.N11Cities;

/// <summary>
/// N11 il kaydı — <b>HOST-GLOBAL</b> adres referansı (IMultiTenant DEĞİL; TenantId yok → tüm tenant'lar paylaşır).
/// N11 CityService.GetCities'ten sync'lenir (81 il). İlçeler <see cref="N11District"/>'te (CityCode ile bağlı);
/// mahalleler on-demand (saklanmaz). Kaynak: SOAP (REST /cdn şehri bilmez).
/// </summary>
public class N11City : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected N11City()
    {
    }

    public N11City(string cityCode, string cityId, string cityName)
    {
        CityCode = StringFieldGuard.EnsureRequiredText(cityCode, nameof(CityCode), 1, N11CityConsts.CodeMaxLength);
        CityId = StringFieldGuard.EnsureRequiredText(cityId, nameof(CityId), 1, N11CityConsts.CodeMaxLength);
        SetName(cityName);
    }

    #endregion

    #region Properties

    /// <summary>N11 il kodu (1–81) — GetDistrict girdisi. Global benzersiz.</summary>
    public string CityCode { get; protected set; } = string.Empty;

    /// <summary>N11 il id'si (ör. 2501).</summary>
    public string CityId { get; protected set; } = string.Empty;

    public string Name { get; protected set; } = string.Empty;

    /// <summary>Çekirdek coğrafyaya gevşek köprü — eşlenen <see cref="Geography.AdministrativeArea"/> id'si (nav YOK;
    /// N11 Country'yi BİLMEZ). GeographySeeder doldurur; null = henüz eşlenmedi.</summary>
    public Guid? CoreAdministrativeAreaId { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, N11CityConsts.NameMaxLength);
    }

    /// <summary>Çekirdek idari alan köprüsünü set eder (boş Guid → null). GeographySeeder çağırır.</summary>
    public virtual void SetCoreAdministrativeArea(Guid? administrativeAreaId)
    {
        CoreAdministrativeAreaId = administrativeAreaId == Guid.Empty ? null : administrativeAreaId;
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}
