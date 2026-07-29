using System;
using System.Linq;
using Integration.Framework.Addressing;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// Şablonun kargo firması satırları. Korunan değişmez: N11 senkronu firma listesini gelen kimliklere göre
/// hizalar — kalan korunur, çıkan düşer, yeni eklenir.
///
/// <para>Firma başına "varsayılan cari alt hesap" bağı 2026-07-28'de kaldırıldı (hiçbir muhasebe akışında
/// okunmuyordu ve canlıda tamamı boştu); o davranışın testleri de bu dosyadan çıktı.</para>
/// </summary>
public class N11ShipmentTemplateCompanyTests
{
    private const string Yurtici = "345";
    private const string Aras = "346";

    private static readonly Guid CompanyId = SimpleGuidGenerator.Instance.Create();
    private static readonly Guid ChannelId = SimpleGuidGenerator.Instance.Create();

    [Fact]
    public void Sync_keeps_companies_that_are_still_listed()
    {
        var template = BuildTemplate();
        template.SetShipmentCompanies(new[] { Yurtici, Aras });

        template.SetShipmentCompanies(new[] { Yurtici, Aras });   // senkron tekrar koştu

        template.Companies.Select(c => c.ExternalId).ShouldBe(new[] { Yurtici, Aras });
    }

    [Fact]
    public void Sync_adds_a_newly_listed_company()
    {
        var template = BuildTemplate();
        template.SetShipmentCompanies(new[] { Yurtici });

        template.SetShipmentCompanies(new[] { Yurtici, Aras });   // N11'e yeni firma eklendi

        template.Companies.Select(c => c.ExternalId).ShouldBe(new[] { Yurtici, Aras });
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
    public void Deactivated_template_keeps_its_companies()
    {
        // N11'den kalkan şablon SİLİNMEZ → pasifleşir; firma listesi kaydın içinde yaşamaya devam eder.
        var template = BuildTemplate();
        template.SetShipmentCompanies(new[] { Yurtici });

        template.SetActive(false);

        template.IsActive.ShouldBeFalse();
        template.Companies.Single().ExternalId.ShouldBe(Yurtici);
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
