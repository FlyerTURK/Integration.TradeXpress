using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.N11Products;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>SKU kimlik/eşleme davranışı (Faz 1) — sellerStockCode DONMASI + synchronizer sil-yeniden-üret
/// senaryosunda kod/imza üzerinden yeniden bağlama + PlanStockCodes'un push-öncesi mutasyonsuzluğu.</summary>
public class SalesChannelTrN11ProductSkuTests
{
    private static SalesChannelTrN11Product NewProduct()
    {
        return new SalesChannelTrN11Product(
            companyId: Guid.NewGuid(),
            salesChannelId: Guid.NewGuid(),
            productId: Guid.NewGuid(),
            sellerCode: "URUN01-1",
            sequenceNo: 1,
            categoryExternalId: "1001",
            shipmentTemplateName: "Sablon");
    }

    private static N11SkuPushCandidate Candidate(Guid variantId, string code, params (string, string)[] attrs)
    {
        return new N11SkuPushCandidate(
            variantId,
            code,
            attrs.Select(a => new SalesChannelTrN11ProductCategoryAttribute(a.Item1, a.Item2)).ToList());
    }

    [Fact]
    public void ReconcileSkus_Should_Freeze_Stock_Code_On_First_Push()
    {
        var product = NewProduct();
        var variantId = Guid.NewGuid();

        var map = product.ReconcileSkus(new[] { Candidate(variantId, "KIRMIZI", ("Renk", "Kırmızı")) });

        map[variantId].SellerStockCode.ShouldBe("KIRMIZI");   // İLK listeleme ÇIPLAK varyant kodu (ChannelSequenceCode)
        product.Skus.ShouldHaveSingleItem();
    }

    /// <summary>Aynı ürünün İKİNCİ listelemesi son ek ALIR — çıplak kod kuralı benzersizliği bozmaz: son ek
    /// yalnız gerçekten ayırt etmesi gereken yerde (SequenceNo ≥ 2) devreye girer.</summary>
    [Fact]
    public void Second_listing_still_gets_a_disambiguating_suffix()
    {
        var second = new SalesChannelTrN11Product(
            companyId: Guid.NewGuid(),
            salesChannelId: Guid.NewGuid(),
            productId: Guid.NewGuid(),
            sellerCode: "URUN01-2",
            sequenceNo: 2,
            categoryExternalId: "1001",
            shipmentTemplateName: "Sablon");

        second.BuildStockCode("KIRMIZI").ShouldBe("KIRMIZI-2");
    }

    [Fact]
    public void ReconcileSkus_Should_Rebind_By_Signature_When_Variant_Regenerated()
    {
        var product = NewProduct();
        var oldVariantId = Guid.NewGuid();

        // İlk push: satır kurulur + snapshot (RecordSkuPush) → imza temeli oluşur.
        product.ReconcileSkus(new[] { Candidate(oldVariantId, "KIRMIZI", ("Renk", "Kırmızı")) });
        product.RecordSkuPush("KIRMIZI", 5, 100m, new[] { new SalesChannelTrN11ProductCategoryAttribute("Renk", "Kırmızı") });

        // Synchronizer varyantı sildi + YENİ id + YENİ kod ile yeniden üretti (aynı seçenek kombinasyonu).
        var newVariantId = Guid.NewGuid();
        var map = product.ReconcileSkus(new[] { Candidate(newVariantId, "KRMZ", ("Renk", "Kırmızı")) });

        product.Skus.ShouldHaveSingleItem();                       // YENİ satır AÇILMADI
        map[newVariantId].SellerStockCode.ShouldBe("KIRMIZI");   // dondurulmuş kod KORUNDU
        map[newVariantId].ProductVariantId.ShouldBe(newVariantId); // yeniden bağlandı
    }

    [Fact]
    public void ReconcileSkus_Should_Rebind_By_Frozen_Code_When_Variant_Recreated_With_Same_Code()
    {
        var product = NewProduct();
        var oldVariantId = Guid.NewGuid();
        product.ReconcileSkus(new[] { Candidate(oldVariantId, "V1", ("Beden", "M")) });

        // Aynı kod, yeni id (imza olmadan da kod eşleşmesi bağlamalı).
        var newVariantId = Guid.NewGuid();
        var map = product.ReconcileSkus(new[] { Candidate(newVariantId, "V1", ("Beden", "M")) });

        product.Skus.ShouldHaveSingleItem();
        map[newVariantId].SellerStockCode.ShouldBe("V1");
    }

    [Fact]
    public void PlanStockCodes_Should_Not_Mutate_Entity()
    {
        var product = NewProduct();
        var variantId = Guid.NewGuid();

        var plan = product.PlanStockCodes(new[] { Candidate(variantId, "V1", ("Beden", "M")) });

        plan[variantId].ShouldBe("V1");
        product.Skus.ShouldBeEmpty();   // push ÖNCESİ plan satır EKLEMEZ (başarısız push bayat kod dondurmasın)
    }

    [Fact]
    public void PlanStockCodes_Should_Reuse_Frozen_Code_For_Existing_Row()
    {
        var product = NewProduct();
        var variantId = Guid.NewGuid();
        product.ReconcileSkus(new[] { Candidate(variantId, "V1", ("Beden", "M")) });

        // Varyant kodu sonradan değişti ama satır zaten donmuş → plan dondurulmuş kodu döndürür.
        var plan = product.PlanStockCodes(new[] { Candidate(variantId, "YENIKOD", ("Beden", "M")) });

        plan[variantId].ShouldBe("V1");
    }

    [Fact]
    public void RecordSkuPush_Then_ApplySkuIdentity_Should_Populate_Row()
    {
        var product = NewProduct();
        var variantId = Guid.NewGuid();
        product.ReconcileSkus(new[] { Candidate(variantId, "V1", ("Beden", "M")) });

        product.RecordSkuPush("V1", 7, 250m, new[] { new SalesChannelTrN11ProductCategoryAttribute("Beden", "M") });
        product.ApplySkuIdentity("V1", n11SkuId: 999, version: 3);

        var sku = product.Skus.Single();
        sku.LastSentQuantity.ShouldBe(7);
        sku.LastSentOptionPrice.ShouldBe(250m);
        sku.N11SkuId.ShouldBe(999);
        sku.N11Version.ShouldBe(3);
        sku.AttributeSnapshot.Single().Value.ShouldBe("M");
    }

    [Fact]
    public void ApplySkuIdentity_Should_Not_Erase_Missing_Fields()
    {
        var product = NewProduct();
        var variantId = Guid.NewGuid();
        product.ReconcileSkus(new[] { Candidate(variantId, "V1") });
        product.ApplySkuIdentity("V1", n11SkuId: 999, version: 3);

        // Sonraki yanıtta version yok → mevcut korunur.
        product.ApplySkuIdentity("V1", n11SkuId: null, version: null);

        var sku = product.Skus.Single();
        sku.N11SkuId.ShouldBe(999);
        sku.N11Version.ShouldBe(3);
    }
}
