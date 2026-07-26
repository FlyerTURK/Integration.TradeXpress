using System;
using System.Linq;
using Integration.Framework.Addressing;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// Şablonun kargo firması satırları + varsayılan cari bağı (2026-07-26 Hakan kararı). Korunan değişmezler:
/// senkron kullanıcı emeğini EZMEZ, aynı firma tek cari gösterir, pasifleşen şablon bağlarını kaybetmez.
/// </summary>
public class N11ShipmentTemplateCompanyTests
{
    private const string Yurtici = "345";
    private const string Aras = "346";

    private static readonly Guid CompanyId = SimpleGuidGenerator.Instance.Create();
    private static readonly Guid ChannelId = SimpleGuidGenerator.Instance.Create();
    private static readonly Guid YurticiSubAccount = SimpleGuidGenerator.Instance.Create();

    [Fact]
    public void Sync_preserves_sub_account_of_kept_company()
    {
        // Kullanıcı Yurtiçi'yi cariye bağladı; sonra N11'den şablon yeniden indi (Yurtiçi hâlâ listede).
        var template = BuildTemplate();
        template.SetShipmentCompanies(new[] { Yurtici, Aras });
        template.SetCompanySubAccount(Yurtici, YurticiSubAccount);

        template.SetShipmentCompanies(new[] { Yurtici, Aras });   // senkron tekrar koştu

        template.Companies.Single(c => c.ExternalId == Yurtici).SubAccountId.ShouldBe(YurticiSubAccount);
    }

    [Fact]
    public void Sync_adds_new_company_as_orphan()
    {
        var template = BuildTemplate();
        template.SetShipmentCompanies(new[] { Yurtici });
        template.SetCompanySubAccount(Yurtici, YurticiSubAccount);

        template.SetShipmentCompanies(new[] { Yurtici, Aras });   // N11'e yeni firma eklendi

        template.Companies.Single(c => c.ExternalId == Aras).SubAccountId.ShouldBeNull();
        template.Companies.Single(c => c.ExternalId == Yurtici).SubAccountId.ShouldBe(YurticiSubAccount);
    }

    [Fact]
    public void Sync_drops_company_removed_from_template()
    {
        var template = BuildTemplate();
        template.SetShipmentCompanies(new[] { Yurtici, Aras });

        template.SetShipmentCompanies(new[] { Yurtici });

        template.Companies.Select(c => c.ExternalId).ShouldBe(new[] { Yurtici });
    }

    [Fact]
    public void Sub_account_can_be_cleared_back_to_orphan()
    {
        var template = BuildTemplate();
        template.SetShipmentCompanies(new[] { Yurtici });
        template.SetCompanySubAccount(Yurtici, YurticiSubAccount);

        template.SetCompanySubAccount(Yurtici, null);

        template.Companies.Single().SubAccountId.ShouldBeNull();
    }

    [Fact]
    public void Setting_sub_account_of_unknown_company_is_ignored()
    {
        // Senkron sırası kullanıcı işlemiyle yarışabilir — şablonda olmayan firmaya bağ kurulmaz, patlamaz da.
        var template = BuildTemplate();
        template.SetShipmentCompanies(new[] { Yurtici });

        template.SetCompanySubAccount(Aras, YurticiSubAccount);

        template.Companies.Single().SubAccountId.ShouldBeNull();
    }

    [Fact]
    public void Deactivated_template_keeps_its_company_links()
    {
        // N11'den kalkan şablon SİLİNMEZ → pasifleşir; kullanıcının kurduğu cari bağı yaşamalı.
        var template = BuildTemplate();
        template.SetShipmentCompanies(new[] { Yurtici });
        template.SetCompanySubAccount(Yurtici, YurticiSubAccount);

        template.SetActive(false);

        template.IsActive.ShouldBeFalse();
        template.Companies.Single().SubAccountId.ShouldBe(YurticiSubAccount);
    }

    [Fact]
    public void Template_starts_active()
    {
        BuildTemplate().IsActive.ShouldBeTrue();
    }

    private static N11ShipmentTemplate BuildTemplate()
    {
        return new N11ShipmentTemplate(
            CompanyId,
            ChannelId,
            "TEST KARGO",
            N11DeliveryFeeType.SellerPays,
            N11ShipmentMethod.Cargo,
            new Address("Depo", "Adres", "34", "Kadıköy", "34000"));
    }
}
