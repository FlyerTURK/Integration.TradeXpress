using System;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// Şablonun KARGO FİRMASI satırı — bu şablonla hangi firmayla gönderildiğini söyler
/// (<see cref="ExternalId"/>, N11 aynasının kimliği). Owned tip (JSON kolonu; sorgulanmaz, N11'e yalnız kimlik
/// push edilir) — repo deseni: <c>SalesChannelEtsyProductListingAttribute</c>.
///
/// <para><b>Cari bağı 2026-07-28'de KALDIRILDI</b> (Hakan): firma başına varsayılan cari alt hesap tutuluyor ve
/// bağlanmamış firmalar kullanıcıya soruluyordu. Bu alan hiçbir muhasebe akışında OKUNMUYORDU (sipariş→fiş
/// köprüsü yok) ve canlıdaki beş şablonun hepsinde boştu; kargo gideri artık şablon/tarife üzerinden
/// fiyatlamaya giriyor. Kargo firmasının carisi gerektiğinde kullanıcı kendi cari planından yönetir.</para>
/// </summary>
public class N11ShipmentTemplateCompany
{
    #region Constructors

    protected N11ShipmentTemplateCompany()
    {
    }

    public N11ShipmentTemplateCompany(string externalId)
    {
        SetExternalId(externalId);
    }

    #endregion

    #region Properties

    /// <summary>N11 kargo firmasının kimliği (<c>N11ShipmentCompany.ExternalId</c>). Ad/kısa kod aynadan okunur —
    /// burada KOPYALANMAZ (tek kaynak ayna).</summary>
    public virtual string ExternalId { get; protected set; } = string.Empty;

    #endregion

    #region Methods

    public virtual void SetExternalId(string externalId)
    {
        ExternalId = StringFieldGuard.EnsureRequiredText(
            externalId, nameof(ExternalId), 1, N11ShipmentConsts.ExternalIdMaxLength);
    }

    public override string ToString()
    {
        return ExternalId;
    }

    #endregion
}
