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

    // K1 KÖPRÜSÜ KALKTI (2026-07-26): çekirdek ShipmentTemplate silindi — kargo artık YALNIZ kanal
    // seviyesinde yaşıyor. Şablon zaten push için self-contained'dı; kopyalanacak bir çekirdek kalmadı.

    /// <summary>Şablon adı = N11 kimliği. (SalesChannelId, TemplateName) benzersiz.</summary>
    public virtual string TemplateName { get; protected set; } = null!;

    public virtual N11DeliveryFeeType DeliveryFeeType { get; protected set; }
    public virtual N11ShipmentMethod ShipmentMethod { get; protected set; }

    public virtual bool SpecialDelivery { get; protected set; }
    public virtual bool CombinedShipmentAllowed { get; protected set; }

    /// <summary>n11 anlaşmalı kargo kullanımı — <b>DAİMA true</b>, kullanıcıya sorulmaz (2026-07-26 Hakan kararı,
    /// canlı API doğrulaması): N11 <c>false</c> push'unu reddediyor —
    /// <i>"İade/Değişim Kargolarında n11.com anlaşması kullanımı zorunluluğu 12.09.2019 tarihinde aktif edilmiştir."</i>
    /// Elimizdeki v4.6 referans dokümanı bu tarihten eski olduğu için <c>false</c> örneği içeriyor; canlı otoritedir.
    /// Alan wire uyumu için duruyor (şema zorunlu), ama seçilebilir bir ayar DEĞİLDİR.</summary>
    public virtual bool UseDmallCargo { get; protected set; } = true;

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

    /// <summary>Şablonun kargo firmaları — her satır firma kimliği + o firmanın varsayılan cari alt hesabı
    /// (<see cref="N11ShipmentTemplateCompany"/>). Sıra N11'den geldiği gibi korunur.</summary>
    public virtual List<N11ShipmentTemplateCompany> Companies { get; protected set; } = new();

    /// <summary>Şablon N11'de hâlâ var mı. Senkron, N11'de bulunmayan şablonu SİLMEZ → pasifleştirir
    /// (2026-07-26 Hakan kararı): şablon kalkmışsa o şablonla iş yapılmıyor demektir, ama kullanıcının kurduğu
    /// cari bağları ve geçmiş referanslar korunmalı. Pasif şablon push edilmez, yeni işte seçilemez.</summary>
    public virtual bool IsActive { get; protected set; } = true;

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

    /// <summary>Kullanıcı seçimli bayraklar. <c>UseDmallCargo</c> BURADA YOK — anlaşmalı kargo zorunlu olduğundan
    /// (bkz. alan notu) daima true'dur, ayar olarak sunulmaz.</summary>
    public virtual void SetFlags(bool specialDelivery, bool combinedShipmentAllowed)
    {
        SpecialDelivery = specialDelivery;
        CombinedShipmentAllowed = combinedShipmentAllowed;
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

    /// <summary>Firma listesini gelen kimliklere göre senkronize eder. <b>Cari bağları KORUNUR:</b> listede kalan
    /// firmanın <c>SubAccountId</c>'sine dokunulmaz (senkron kullanıcı emeğini ezmez); listeden çıkan firma satırı
    /// düşer, yeni firma ÖKSÜZ (carisi boş) eklenir ve kullanıcıya sorulacak listeye girer.</summary>
    public virtual void SetShipmentCompanies(IEnumerable<string> shipmentCompanyExternalIds)
    {
        var incoming = NormalizeRefs(shipmentCompanyExternalIds);
        var existing = Companies.ToDictionary(c => c.ExternalId, StringComparer.Ordinal);

        Companies = incoming
            .Select(id => existing.TryGetValue(id, out var kept) ? kept : new N11ShipmentTemplateCompany(id))
            .ToList();
    }

    /// <summary>Bir kargo firmasının varsayılan cari alt hesabını bağlar. Firma bu şablonda yoksa sessizce
    /// yok sayılır (senkron sırası kullanıcı işlemiyle yarışabilir).</summary>
    public virtual void SetCompanySubAccount(string externalId, Guid? subAccountId)
    {
        var target = Companies.FirstOrDefault(c => string.Equals(c.ExternalId, externalId, StringComparison.Ordinal));
        target?.SetSubAccount(subAccountId);
    }

    /// <summary>Şablonu aktif/pasif yapar — senkron, N11'de bulunmayanı pasifleştirir; geri gelirse aktifleşir.</summary>
    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    /// <summary>Teslimat illerini ayarlar — <b>BOŞ BIRAKILAMAZ</b> (2026-07-26 Hakan kararı, dokümanla doğrulandı):
    /// <i>"deliverableCities alanı boş olursa ya da hiç olmazsa; ilgili kargo şablonu ile hiçbir şehre teslimat
    /// gerçekleştirilemez olarak kaydedilecektir."</i> Yani boş liste "tüm iller" DEĞİL "hiçbir il" demektir —
    /// sessizce işlevsiz bir şablon üretmemek için burada fail-fast edilir.</summary>
    public virtual void SetDeliverableCities(IEnumerable<string> cityCodes)
    {
        var normalized = NormalizeRefs(cityCodes);
        if (normalized.Count == 0)
        {
            throw new BusinessException("TradeXpress:N11:Shipment:DeliverableCitiesRequired");
        }

        DeliverableCityCodes = normalized;
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
