using System;
using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Addressing;
using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// N11 kargo/teslimat şablonu — <b>per-satıcı (company-owned + per-tenant)</b>. Bizde tutulur + N11'e push edilir
/// (<c>CreateOrUpdateShipmentTemplate</c>, kanalın KENDİ kimliğiyle). Kimlik N11'de <see cref="TemplateName"/>
/// (ayrı id yok) → bizde (SalesChannelId, TemplateName) benzersiz. Kargo firmaları + iller + iade firması
/// host-global referanslara (<see cref="Integration.TradeXpress.N11Shipments.N11ShipmentCompany"/> / N11City)
/// id-only bağlanır. Adresler yeniden-kullanılabilir <see cref="Address"/> value object (OwnsOne). N11'de silme
/// operasyonu YOK → yerel silme (N11'de kalır).
/// </summary>
public class N11ShipmentTemplate : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected N11ShipmentTemplate()
    {
    }

    public N11ShipmentTemplate(
        Guid companyId,
        Guid salesChannelId,
        string templateName,
        N11DeliveryFeeType deliveryFeeType,
        N11ShipmentMethod shipmentMethod,
        Address warehouseAddress)
    {
        SetCompany(companyId);
        SetSalesChannel(salesChannelId);
        SetTemplateName(templateName);
        DeliveryFeeType = deliveryFeeType;
        ShipmentMethod = shipmentMethod;
        SetWarehouseAddress(warehouseAddress);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket (güvenlik sınırı, set-once).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip satış kanalı (id-only, set-once).</summary>
    public virtual Guid SalesChannelId { get; protected set; }

    /// <summary>Şablon adı = N11 kimliği. (SalesChannelId, TemplateName) benzersiz.</summary>
    public virtual string TemplateName { get; protected set; } = null!;

    public virtual N11DeliveryFeeType DeliveryFeeType { get; protected set; }
    public virtual N11ShipmentMethod ShipmentMethod { get; protected set; }

    public virtual bool SpecialDelivery { get; protected set; }
    public virtual bool CombinedShipmentAllowed { get; protected set; }
    public virtual bool UseDmallCargo { get; protected set; }

    public virtual string? ShippingInfo { get; protected set; }
    public virtual string? ExchangeInfo { get; protected set; }
    public virtual string? InstallmentInfo { get; protected set; }
    public virtual string? CargoAccountNo { get; protected set; }

    /// <summary>Şartlı kargo eşiği (N11 panel "Şartlı Kargo") — bu tutar/adet üzeri ücretsiz kargo. Null = şartlı kargo yok.
    /// N11'de adres elementine gömülü (<c>feeConditionPrice</c>); bizde şablon-düzeyinde tutulur.</summary>
    public virtual decimal? ConditionalShippingThreshold { get; protected set; }

    /// <summary>Şartlı kargo eşiğinin birimi (TL/adet). Eşik null ise anlamsız (varsayılan <see cref="N11ConditionalShippingUnit.Amount"/>).</summary>
    public virtual N11ConditionalShippingUnit ConditionalShippingUnit { get; protected set; } = N11ConditionalShippingUnit.Amount;

    /// <summary>İade/talep kargosu firması (N11 kargo firması ExternalId; id-only, opsiyonel).</summary>
    public virtual string? ClaimShipmentCompanyExternalId { get; protected set; }

    /// <summary>Depo (gönderici) adresi — zorunlu. Yeniden-kullanılabilir <see cref="Address"/> (OwnsOne).</summary>
    public virtual Address WarehouseAddress { get; protected set; } = null!;

    /// <summary>Değişim/iade adresi — opsiyonel (OwnsOne).</summary>
    public virtual Address? ExchangeAddress { get; protected set; }

    /// <summary>Şablonun kargo firmaları (N11 kargo firması ExternalId'leri; id-only liste).</summary>
    public virtual List<string> ShipmentCompanyExternalIds { get; protected set; } = new();

    /// <summary>Teslimat yapılan iller (N11 il kodları; id-only liste).</summary>
    public virtual List<string> DeliverableCityCodes { get; protected set; } = new();

    #endregion

    #region Methods

    public virtual void SetTemplateName(string templateName)
    {
        TemplateName = StringFieldGuard.EnsureRequiredText(templateName, nameof(TemplateName), 1, N11ShipmentConsts.TemplateNameMaxLength);
    }

    public virtual void SetDeliveryFeeType(N11DeliveryFeeType deliveryFeeType)
    {
        DeliveryFeeType = deliveryFeeType;
    }

    public virtual void SetShipmentMethod(N11ShipmentMethod shipmentMethod)
    {
        ShipmentMethod = shipmentMethod;
    }

    public virtual void SetFlags(bool specialDelivery, bool combinedShipmentAllowed, bool useDmallCargo)
    {
        SpecialDelivery = specialDelivery;
        CombinedShipmentAllowed = combinedShipmentAllowed;
        UseDmallCargo = useDmallCargo;
    }

    public virtual void SetInfos(string? shippingInfo, string? exchangeInfo, string? installmentInfo)
    {
        ShippingInfo = StringFieldGuard.EnsureOptionalText(shippingInfo, nameof(ShippingInfo), 1, N11ShipmentConsts.InfoMaxLength);
        ExchangeInfo = StringFieldGuard.EnsureOptionalText(exchangeInfo, nameof(ExchangeInfo), 1, N11ShipmentConsts.InfoMaxLength);
        InstallmentInfo = StringFieldGuard.EnsureOptionalText(installmentInfo, nameof(InstallmentInfo), 1, N11ShipmentConsts.InfoMaxLength);
    }

    public virtual void SetCargoAccountNo(string? cargoAccountNo)
    {
        CargoAccountNo = StringFieldGuard.EnsureOptionalText(cargoAccountNo, nameof(CargoAccountNo), 1, N11ShipmentConsts.CargoAccountNoMaxLength);
    }

    /// <summary>Şartlı kargo eşiğini + birimini ayarlar. Eşik null = şartlı kargo kapalı (birim varsayılana çekilir).
    /// Negatif eşik geçersiz (fail-fast).</summary>
    public virtual void SetConditionalShipping(decimal? threshold, N11ConditionalShippingUnit unit)
    {
        if (threshold is { } value && value < 0)
        {
            throw new BusinessException("TradeXpress:N11:Shipment:ConditionalShippingNegative");
        }

        ConditionalShippingThreshold = threshold;
        ConditionalShippingUnit = threshold is null ? N11ConditionalShippingUnit.Amount : unit;
    }

    public virtual void SetClaimShipmentCompany(string? shipmentCompanyExternalId)
    {
        ClaimShipmentCompanyExternalId = StringFieldGuard.EnsureOptionalText(
            shipmentCompanyExternalId, nameof(ClaimShipmentCompanyExternalId), 1, N11ShipmentConsts.ExternalIdMaxLength);
    }

    public virtual void SetWarehouseAddress(Address warehouseAddress)
    {
        if (warehouseAddress is null)
        {
            throw new RequiredPropertyException(nameof(WarehouseAddress));
        }

        WarehouseAddress = warehouseAddress;
    }

    public virtual void SetExchangeAddress(Address? exchangeAddress)
    {
        ExchangeAddress = exchangeAddress;
    }

    public virtual void SetShipmentCompanies(IEnumerable<string> shipmentCompanyExternalIds)
    {
        ShipmentCompanyExternalIds = NormalizeRefs(shipmentCompanyExternalIds);
    }

    public virtual void SetDeliverableCities(IEnumerable<string> cityCodes)
    {
        DeliverableCityCodes = NormalizeRefs(cityCodes);
    }

    public override string ToString()
    {
        return TemplateName;
    }

    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    private void SetSalesChannel(Guid salesChannelId)
    {
        if (salesChannelId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelId));
        }

        SalesChannelId = salesChannelId;
    }

    /// <summary>Id-only referans listesini normalize eder: boşları at, trim, tekilleştir (sıra korunur).</summary>
    private static List<string> NormalizeRefs(IEnumerable<string>? ids)
    {
        if (ids is null)
        {
            return new List<string>();
        }

        return ids
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    #endregion
}
