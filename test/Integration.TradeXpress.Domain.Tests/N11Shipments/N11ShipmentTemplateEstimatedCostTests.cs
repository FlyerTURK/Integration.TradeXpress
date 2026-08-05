using System;
using Integration.Framework.Addressing;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// Şablonun TAHMİNİ KARGO MALİYETİ — yalnız yerel fiyatlama alanı (N11'de karşılığı yoktur, push edilmez).
///
/// <para><b>Neden eklendi:</b> reçetedeki kargo satırı kanal geneli tek düz sayıydı; üç farklı şablonu olan
/// satıcının bütün ürünleri aynı kargo rakamıyla fiyatlanıyordu. Bu alan dolduğunda o şablonu kullanan ürünlerin
/// kargo satırı buradan beslenir.</para>
/// </summary>
public class N11ShipmentTemplateEstimatedCostTests
{
    private static readonly Guid CompanyId = SimpleGuidGenerator.Instance.Create();
    private static readonly Guid ChannelId = SimpleGuidGenerator.Instance.Create();
    private static readonly Guid TryUnitId = SimpleGuidGenerator.Instance.Create();

    [Fact]
    public void Template_starts_without_an_estimated_cost()
    {
        // Varsayılan BOŞ — "kargo bedava" varsaymıyoruz; alan doldurulana dek kanal gider değeri geçerli.
        var template = BuildTemplate();

        template.EstimatedCost.ShouldBeNull();
        template.EstimatedCostCurrencyUnitId.ShouldBeNull();
    }

    [Fact]
    public void Estimated_cost_is_stored_with_its_currency()
    {
        var template = BuildTemplate();

        template.SetEstimatedCost(85m, TryUnitId);

        template.EstimatedCost.ShouldBe(85m);
        template.EstimatedCostCurrencyUnitId.ShouldBe(TryUnitId);
    }

    /// <summary>Sıfır BOŞA çevrilir. Gerekçe: 0 "bu şablonla gönderi bedava" iddiasıdır ve composer sıfır
    /// operandlı satır üretmediği için kargo reçeteden SESSİZCE düşerdi — kullanıcı alanı temizlemek isterken
    /// farkında olmadan kargoyu fiyattan çıkarmış olurdu. Boş bırakmak ise açıkça "kanal değerini kullan" demek.</summary>
    [Fact]
    public void Zero_is_normalised_to_empty_so_shipping_never_silently_disappears()
    {
        var template = BuildTemplate();
        template.SetEstimatedCost(85m, TryUnitId);

        template.SetEstimatedCost(0m, TryUnitId);

        template.EstimatedCost.ShouldBeNull();
        template.EstimatedCostCurrencyUnitId.ShouldBeNull();   // tutar yoksa birim de anlamsız
    }

    [Fact]
    public void Clearing_the_cost_also_clears_the_currency()
    {
        var template = BuildTemplate();
        template.SetEstimatedCost(85m, TryUnitId);

        template.SetEstimatedCost(null, TryUnitId);

        template.EstimatedCost.ShouldBeNull();
        template.EstimatedCostCurrencyUnitId.ShouldBeNull();
    }

    [Fact]
    public void Negative_cost_is_rejected()
    {
        var template = BuildTemplate();

        Should.Throw<BusinessException>(() => template.SetEstimatedCost(-1m, TryUnitId))
            .Code.ShouldBe("TradeXpress:N11:Shipment:EstimatedCostNegative");
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
