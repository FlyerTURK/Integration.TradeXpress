using System;
using System.Linq;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="SideCostSettingsJson"/> testleri (saf, DB'siz) — TPT nedeniyle EF native ToJson yerine kullanılan
/// değer-dönüştürücünün, VO'nun PROTECTED ctor/setter'larına rağmen (contract-modifier) kayıpsız yazıp okuduğunu
/// kanıtlar. Sessiz veri kaybı (deserialize'da default'a düşme) burada KIRMIZI yanar.
/// AYRICA: eski sabit-alanlı payload'ın (2026-07-10 öncesi şema) gider satırlarına TOLERANSLI dönüşümü —
/// kullanıcının test verisi (tutarlar + hizmet/cari bağları) kaybolmaz.
/// </summary>
public class SideCostSettingsJsonTests
{
    [Fact]
    public void Roundtrip_preserves_all_item_fields()
    {
        var serviceId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var subAccountId = Guid.NewGuid();
        var usdId = Guid.NewGuid();

        var original = new SideCostSettings(new[]
        {
            new SideCostItem(
                SideCostKind.Packaging, "Kutu + Dolgu", SideCostCalcMode.FixedAmount, 12.5m,
                usdId, serviceId, SideCostPostingMode.Expense, null, null,
                autoRate: false, isEnabled: true, displayOrder: 0, requiresVariantOptIn: false),
            new SideCostItem(
                SideCostKind.InsuredShipping, null, SideCostCalcMode.PercentOfCost, 2m,
                null, serviceId, SideCostPostingMode.CounterpartyAccount, accountId, subAccountId,
                autoRate: false, isEnabled: false, displayOrder: 1, requiresVariantOptIn: true),
            new SideCostItem(
                SideCostKind.Commission, null, SideCostCalcMode.GrossUpPercent, 21.5m,
                null, null, SideCostPostingMode.CounterpartyAccount, accountId, null,
                autoRate: true, isEnabled: true, displayOrder: 2, requiresVariantOptIn: false),
        });

        var roundtripped = SideCostSettingsJson.Deserialize(SideCostSettingsJson.Serialize(original));

        roundtripped.ShouldNotBeNull();
        roundtripped.Items.Count.ShouldBe(3);

        var packaging = roundtripped.Items[0];
        packaging.Kind.ShouldBe(SideCostKind.Packaging);
        packaging.DisplayName.ShouldBe("Kutu + Dolgu");
        packaging.CalcMode.ShouldBe(SideCostCalcMode.FixedAmount);
        packaging.Value.ShouldBe(12.5m);
        packaging.CurrencyUnitId.ShouldBe(usdId);
        packaging.ServiceId.ShouldBe(serviceId);
        packaging.PostingMode.ShouldBe(SideCostPostingMode.Expense);

        var insured = roundtripped.Items[1];
        insured.CalcMode.ShouldBe(SideCostCalcMode.PercentOfCost);
        insured.AccountId.ShouldBe(accountId);
        insured.SubAccountId.ShouldBe(subAccountId);
        insured.IsEnabled.ShouldBeFalse();
        insured.RequiresVariantOptIn.ShouldBeTrue();
        insured.DisplayOrder.ShouldBe(1);

        var commission = roundtripped.Items[2];
        commission.Kind.ShouldBe(SideCostKind.Commission);
        commission.CalcMode.ShouldBe(SideCostCalcMode.GrossUpPercent);
        commission.Value.ShouldBe(21.5m);
        commission.AutoRate.ShouldBeTrue();
    }

    [Fact]
    public void Null_and_empty_json_deserialize_to_null()
    {
        SideCostSettingsJson.Serialize(null).ShouldBeNull();
        SideCostSettingsJson.Deserialize(null).ShouldBeNull();
        SideCostSettingsJson.Deserialize(string.Empty).ShouldBeNull();
    }

    // ── Eski şema toleransı: sabit-alanlı payload → gider satırları (kullanıcı test verisi kaybolmaz) ──

    [Fact]
    public void Legacy_payload_is_converted_to_items()
    {
        var serviceId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var subAccountId = Guid.NewGuid();
        var usdId = Guid.NewGuid();

        // Eski serileştiricinin yazdığı biçim: enum'lar SAYI, PascalCase alanlar, 4 sabit fiş hedefi.
        var legacyJson = $$"""
        {
            "PackagingCost": 60,
            "CargoCost": 40,
            "InsuredShippingMode": 2,
            "InsuredShippingValue": 2,
            "DefaultCommissionRate": 20,
            "PerSaleFixedFee": 0.45,
            "ExtraFeeRate": 3,
            "CostCurrencyUnitId": "{{usdId}}",
            "Packaging": { "PostingMode": 2, "ServiceId": "{{serviceId}}", "AccountId": null, "SubAccountId": null },
            "Cargo": { "PostingMode": 1, "ServiceId": "{{serviceId}}", "AccountId": "{{accountId}}", "SubAccountId": "{{subAccountId}}" },
            "InsuredShipping": { "PostingMode": 1, "ServiceId": null, "AccountId": "{{accountId}}", "SubAccountId": null },
            "Commission": { "PostingMode": 1, "ServiceId": "{{serviceId}}", "AccountId": "{{accountId}}", "SubAccountId": null }
        }
        """;

        var converted = SideCostSettingsJson.Deserialize(legacyJson);

        converted.ShouldNotBeNull();
        converted.Items.Count.ShouldBe(6);   // paketleme + kargo + sigortalı + kanal-sabit + komisyon + Offsite Ads

        var packaging = converted.Items.Single(i => i.Kind == SideCostKind.Packaging);
        packaging.CalcMode.ShouldBe(SideCostCalcMode.FixedAmount);
        packaging.Value.ShouldBe(60m);
        packaging.CurrencyUnitId.ShouldBe(usdId);
        packaging.ServiceId.ShouldBe(serviceId);
        packaging.PostingMode.ShouldBe(SideCostPostingMode.Expense);
        packaging.IsEnabled.ShouldBeTrue();

        var cargo = converted.Items.Single(i => i.Kind == SideCostKind.Cargo);
        cargo.Value.ShouldBe(40m);
        cargo.PostingMode.ShouldBe(SideCostPostingMode.CounterpartyAccount);
        cargo.AccountId.ShouldBe(accountId);
        cargo.SubAccountId.ShouldBe(subAccountId);

        // Sigortalı gönderim: eski PercentOfValue → PercentOfCost; varyant opt-in genellemesiyle işaretli;
        // yüzde modunda para birimi taşınmaz.
        var insured = converted.Items.Single(i => i.Kind == SideCostKind.InsuredShipping);
        insured.CalcMode.ShouldBe(SideCostCalcMode.PercentOfCost);
        insured.Value.ShouldBe(2m);
        insured.CurrencyUnitId.ShouldBeNull();
        insured.RequiresVariantOptIn.ShouldBeTrue();
        insured.AccountId.ShouldBe(accountId);

        var channelFixed = converted.Items.Single(i => i.Kind == SideCostKind.ChannelFixed);
        channelFixed.Value.ShouldBe(0.45m);
        channelFixed.CurrencyUnitId.ShouldBe(usdId);
        channelFixed.ServiceId.ShouldBe(serviceId);

        // Komisyon: eski davranış "çözülmüş oran ?? kanal varsayılanı" → AutoRate=true + Value=fallback.
        var commissions = converted.Items.Where(i => i.Kind == SideCostKind.Commission).ToList();
        commissions.Count.ShouldBe(2);
        var commission = commissions.Single(i => i.AutoRate);
        commission.CalcMode.ShouldBe(SideCostCalcMode.GrossUpPercent);
        commission.Value.ShouldBe(20m);
        commission.ServiceId.ShouldBe(serviceId);

        // Etsy Offsite Ads: eskiden komisyona EKLENEN oran → AYRI GrossUp satırı (adlı; AutoRate kapalı).
        var offsiteAds = commissions.Single(i => !i.AutoRate);
        offsiteAds.DisplayName.ShouldBe("Offsite Ads");
        offsiteAds.Value.ShouldBe(3m);

        // Dönüşüm SONRASI yazım yeni şemadır — ikinci tur kayıpsız (round-trip stabilitesi).
        var rewritten = SideCostSettingsJson.Deserialize(SideCostSettingsJson.Serialize(converted));
        rewritten.ShouldNotBeNull();
        rewritten.Items.Count.ShouldBe(6);
        rewritten.Items.Single(i => i.Kind == SideCostKind.Packaging).Value.ShouldBe(60m);
    }

    [Fact]
    public void Legacy_payload_skips_untouched_entries_but_keeps_service_links()
    {
        // Yalnız kargo tutarı girilmiş + komisyon hedefine hizmet bağlanmış (oran boş) bir eski kayıt:
        // dokunulmamış kalemler satır üretmez; hizmet bağı olan komisyon Value=0 fallback ile taşınır.
        var serviceId = Guid.NewGuid();
        var legacyJson = $$"""
        {
            "PackagingCost": null,
            "CargoCost": 25,
            "InsuredShippingMode": 0,
            "InsuredShippingValue": null,
            "DefaultCommissionRate": null,
            "PerSaleFixedFee": null,
            "ExtraFeeRate": null,
            "CostCurrencyUnitId": null,
            "Packaging": { "PostingMode": 2, "ServiceId": null, "AccountId": null, "SubAccountId": null },
            "Cargo": { "PostingMode": 1, "ServiceId": null, "AccountId": null, "SubAccountId": null },
            "InsuredShipping": { "PostingMode": 1, "ServiceId": null, "AccountId": null, "SubAccountId": null },
            "Commission": { "PostingMode": 1, "ServiceId": "{{serviceId}}", "AccountId": null, "SubAccountId": null }
        }
        """;

        var converted = SideCostSettingsJson.Deserialize(legacyJson);

        converted.ShouldNotBeNull();
        converted.Items.Count.ShouldBe(2);
        converted.Items.Single(i => i.Kind == SideCostKind.Cargo).Value.ShouldBe(25m);

        var commission = converted.Items.Single(i => i.Kind == SideCostKind.Commission);
        commission.ServiceId.ShouldBe(serviceId);
        commission.Value.ShouldBe(0m);
        commission.AutoRate.ShouldBeTrue();
    }

    [Fact]
    public void Legacy_payload_with_grossup_total_over_limit_disables_the_overflowing_item()
    {
        // Eski şemada toplam guard'ı YOKTU (yalnız kalem-başı sınır) — 60 + 50 kaydedilebilmişti. Yeni
        // SideCostSettings ctor'unun Σ-guard'ı OKUMA anında fırlasaydı kanal kaydı liste/edit'te hiç
        // açılamazdı; dönüşüm taşan kalemi IsEnabled=false ile taşır (veri korunur, kullanıcı düzeltir).
        var legacyJson = """
        {
            "PackagingCost": null,
            "CargoCost": null,
            "InsuredShippingMode": 0,
            "InsuredShippingValue": null,
            "DefaultCommissionRate": 60,
            "PerSaleFixedFee": null,
            "ExtraFeeRate": 50,
            "CostCurrencyUnitId": null,
            "Packaging": null,
            "Cargo": null,
            "InsuredShipping": null,
            "Commission": { "PostingMode": 1, "ServiceId": null, "AccountId": null, "SubAccountId": null }
        }
        """;

        var converted = SideCostSettingsJson.Deserialize(legacyJson);

        converted.ShouldNotBeNull();
        var commissions = converted.Items.Where(i => i.Kind == SideCostKind.Commission).ToList();
        commissions.Count.ShouldBe(2);

        var commission = commissions.Single(i => i.AutoRate);
        commission.Value.ShouldBe(60m);
        commission.IsEnabled.ShouldBeTrue();

        var offsiteAds = commissions.Single(i => !i.AutoRate);
        offsiteAds.Value.ShouldBe(50m);          // veri korunur...
        offsiteAds.IsEnabled.ShouldBeFalse();    // ...ama aktif Σ sınırı aşmasın diye kapalı taşınır
    }
}
