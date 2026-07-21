namespace Integration.TradeXpress.Shipments;

/// <summary>
/// Kargo firması — <b>çekirdek HOST-GLOBAL</b> kanal-nötr referans (IMultiTenant DEĞİL; TenantId yok → tüm
/// tenant'lar paylaşır; <see cref="Geography.AdministrativeArea"/>/N11City deseniyle hizalı). Lean kimlik:
/// kaynak/kısa kod (<see cref="Code"/>, N11 ShortName'den türer, ör. ARAS/YK) + görüntü adı (<see cref="Name"/>).
/// Kanal firmaları (<c>N11ShipmentCompany</c>) buna id-only <b>gevşek köprü</b> ile bağlanır (nav YOK, FK YOK);
/// CarrierSeeder N11 firmalarından türetip köprüyü doldurur.
/// </summary>
public class Carrier : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected Carrier()
    {
    }

    public Carrier(string code, string name)
    {
        SetCode(code);
        SetName(name);
    }

    #endregion

    #region Properties

    /// <summary>Kaynak/kısa kod (N11 ShortName'den türer, ör. ARAS/YK). Kültür-bağımsız UPPER, boşluk yok.
    /// ZORUNLU, global benzersiz.</summary>
    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    #endregion

    #region Methods

    public virtual void SetCode(string code)
    {
        // Kaynak kod (N11 ShortName): kültür-bağımsız UPPER, boşluk yok. Min 1 (kısa kodlar, ör. "YK").
        Code = StringFieldGuard.NormalizeInvariantCode(code, nameof(Code), 1, CarrierConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        // N11 firma adı olduğu gibi korunur (Trim + zorunlu); TitleCase YOK (Türkçe karakter kaçağı riski).
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, CarrierConsts.NameMaxLength);
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}
