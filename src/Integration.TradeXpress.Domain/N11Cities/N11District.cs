using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Integration.TradeXpress.N11Cities;

/// <summary>
/// N11 ilçe kaydı — <b>HOST-GLOBAL</b> adres referansı (IMultiTenant DEĞİL). N11 CityService.GetDistrict(cityCode)'ten
/// sync'lenir (~970 ilçe). İline <see cref="CityCode"/> (id-only) ile bağlı. Mahalleler on-demand (saklanmaz;
/// GetNeighborhoods(districtId)). Kaynak: SOAP.
/// </summary>
public class N11District : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected N11District()
    {
    }

    public N11District(string districtId, string cityCode, string name)
    {
        DistrictId = StringFieldGuard.EnsureRequiredText(districtId, nameof(DistrictId), 1, N11CityConsts.CodeMaxLength);
        CityCode = StringFieldGuard.EnsureRequiredText(cityCode, nameof(CityCode), 1, N11CityConsts.CodeMaxLength);
        SetName(name);
    }

    #endregion

    #region Properties

    /// <summary>N11 ilçe id'si (ör. 22969) — GetNeighborhoods girdisi. Global benzersiz.</summary>
    public string DistrictId { get; protected set; } = string.Empty;

    /// <summary>Bağlı il kodu (id-only referans; <see cref="N11City.CityCode"/>).</summary>
    public string CityCode { get; protected set; } = string.Empty;

    public string Name { get; protected set; } = string.Empty;

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, N11CityConsts.NameMaxLength);
    }

    public virtual void SetCityCode(string cityCode)
    {
        CityCode = StringFieldGuard.EnsureRequiredText(cityCode, nameof(CityCode), 1, N11CityConsts.CodeMaxLength);
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}
