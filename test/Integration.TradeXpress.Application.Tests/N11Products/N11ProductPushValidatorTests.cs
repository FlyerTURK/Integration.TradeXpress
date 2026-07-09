using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Products;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>N11 push-öncesi kategori-farkındalıklı validasyon kuralları (Faz 1) — saf sınıf, DI'sız test.
/// Kategori sözleşmesi: varyant eksenlerini kategori belirler; customValue=false değer listeden birebir;
/// zorunlu eksen her SKU'da dolu; SKU'lar arası eksen seti tutarlı ve imzalar benzersiz.</summary>
public class N11ProductPushValidatorTests
{
    private readonly N11ProductPushValidator _validator = new();

    private static N11LeafAttributes ClothingLeaf()
    {
        // Giyim benzeri: Beden variant+mandatory (sabit liste), Renk variant DEĞİL (grouping ekseni), Marka serbest.
        return new N11LeafAttributes("1209218", "Elbise", new List<N11AttributeDef>
        {
            new("1", "Beden", IsMandatory: true, IsVariant: true, IsCustomValue: false, Priority: 1,
                Values: new List<N11AttributeValue> { new("10", "S"), new("11", "M"), new("12", "L") }),
            new("2", "Renk", IsMandatory: false, IsVariant: false, IsCustomValue: true, Priority: 2,
                Values: new List<N11AttributeValue>()),
            new("3", "Marka", IsMandatory: true, IsVariant: false, IsCustomValue: true, Priority: 0,
                Values: new List<N11AttributeValue>()),
        });
    }

    private static N11SkuPushCandidate Candidate(string code, params (string Name, string Value)[] attributes)
    {
        return new N11SkuPushCandidate(
            Guid.NewGuid(),
            code,
            attributes.Select(a => new SalesChannelTrN11ProductCategoryAttribute(a.Name, a.Value)).ToList());
    }

    [Fact]
    public void Should_Pass_And_Canonicalize_Valid_Variants()
    {
        // "s"/"m" küçük yazılmış → listedeki kanonik "S"/"M" ile gönderilmeli (Türkçe-duyarsız eşleşme).
        var result = _validator.Validate(
            ClothingLeaf(),
            new List<SalesChannelTrN11ProductCategoryAttribute> { new("Marka", "TestMarka") },
            new List<N11SkuPushCandidate> { Candidate("V1", ("Beden", "s")), Candidate("V2", ("beden", "m")) });

        result.VariantOptions.Values.SelectMany(x => x).Select(p => p.Value).ShouldBe(new[] { "S", "M" });
        result.VariantOptions.Values.SelectMany(x => x).Select(p => p.Name).ShouldAllBe(n => n == "Beden");
        result.ProductAttributes.Single().Name.ShouldBe("Marka");
    }

    [Fact]
    public void Should_Reject_Axis_Not_In_Category_Variant_Set()
    {
        // Giyimde Renk variant=false → SKU ekseni olarak gönderilemez (grup-ürün mekanizmasına sessiz bölme yok).
        var exception = Should.Throw<BusinessException>(() => _validator.Validate(
            ClothingLeaf(),
            new List<SalesChannelTrN11ProductCategoryAttribute>(),
            new List<N11SkuPushCandidate> { Candidate("V1", ("Beden", "S"), ("Renk", "Mavi")) }));

        exception.Code.ShouldBe("TradeXpress:N11:Product:VariantAxisNotAllowed");
    }

    [Fact]
    public void Should_Reject_Missing_Mandatory_Axis()
    {
        var exception = Should.Throw<BusinessException>(() => _validator.Validate(
            ClothingLeaf(),
            new List<SalesChannelTrN11ProductCategoryAttribute>(),
            new List<N11SkuPushCandidate> { Candidate("V1") }));

        exception.Code.ShouldBe("TradeXpress:N11:Product:VariantAxisMissing");
    }

    [Fact]
    public void Should_Reject_Value_Not_In_List()
    {
        var exception = Should.Throw<BusinessException>(() => _validator.Validate(
            ClothingLeaf(),
            new List<SalesChannelTrN11ProductCategoryAttribute>(),
            new List<N11SkuPushCandidate> { Candidate("V1", ("Beden", "XXL")) }));

        exception.Code.ShouldBe("TradeXpress:N11:Product:AttributeValueNotInList");
    }

    [Fact]
    public void Should_Reject_Inconsistent_Axis_Sets()
    {
        // Zorunlu ekseni olmayan kategori kur (tutarsızlık kuralı zorunluluktan bağımsız yakalanmalı).
        var leaf = new N11LeafAttributes("1", "Test", new List<N11AttributeDef>
        {
            new("1", "Numara", IsMandatory: false, IsVariant: true, IsCustomValue: true, Priority: null,
                Values: new List<N11AttributeValue>()),
        });

        var exception = Should.Throw<BusinessException>(() => _validator.Validate(
            leaf,
            new List<SalesChannelTrN11ProductCategoryAttribute>(),
            new List<N11SkuPushCandidate> { Candidate("V1", ("Numara", "42")), Candidate("V2") }));

        exception.Code.ShouldBe("TradeXpress:N11:Product:VariantAttributesInconsistent");
    }

    [Fact]
    public void Should_Reject_Duplicate_Variant_Signatures()
    {
        var exception = Should.Throw<BusinessException>(() => _validator.Validate(
            ClothingLeaf(),
            new List<SalesChannelTrN11ProductCategoryAttribute>(),
            new List<N11SkuPushCandidate> { Candidate("V1", ("Beden", "S")), Candidate("V2", ("Beden", "s")) }));

        exception.Code.ShouldBe("TradeXpress:N11:Product:DuplicateVariantSignature");
    }

    [Fact]
    public void Should_Reject_Multiple_Variants_When_Category_Has_No_Axis()
    {
        var leaf = new N11LeafAttributes("1", "Külçe Altın", new List<N11AttributeDef>
        {
            new("1", "Marka", IsMandatory: true, IsVariant: false, IsCustomValue: true, Priority: null,
                Values: new List<N11AttributeValue>()),
        });

        var exception = Should.Throw<BusinessException>(() => _validator.Validate(
            leaf,
            new List<SalesChannelTrN11ProductCategoryAttribute>(),
            new List<N11SkuPushCandidate> { Candidate("V1"), Candidate("V2") }));

        exception.Code.ShouldBe("TradeXpress:N11:Product:CategoryHasNoVariantAxis");
    }

    [Fact]
    public void Should_Filter_Variant_Axis_From_Product_Level_Attributes()
    {
        // Eski kayıtlarda ürün-seviyesinde birikmiş "Beden" push'ta sessizce filtrelenir (SKU'larla gider);
        // zorunlu Marka doldurulmuş, Renk ürün-seviyesinde kalır.
        var result = _validator.Validate(
            ClothingLeaf(),
            new List<SalesChannelTrN11ProductCategoryAttribute> { new("Marka", "TestMarka"), new("Beden", "S"), new("Renk", "Mavi") },
            new List<N11SkuPushCandidate> { Candidate("V1", ("Beden", "S")) });

        result.ProductAttributes.Select(p => p.Name).ShouldBe(new[] { "Marka", "Renk" });
    }

    [Fact]
    public void Should_Reject_Missing_Mandatory_Product_Attribute()
    {
        // Marka zorunlu (mandatory + variant DEĞİL) ama ürün-seviyesinde yok → N11'e gitmeden fail-fast.
        var exception = Should.Throw<BusinessException>(() => _validator.Validate(
            ClothingLeaf(),
            new List<SalesChannelTrN11ProductCategoryAttribute>(),
            new List<N11SkuPushCandidate> { Candidate("V1", ("Beden", "S")) }));

        exception.Code.ShouldBe("TradeXpress:N11:Product:ProductAttributeMissing");
    }

    [Fact]
    public void Should_Fold_Turkish_Dotted_I_When_Matching_Values()
    {
        // Türkçe katlama: valueList 'İpek' iken kullanıcı 'i̇pek'/'İPEK' yazsa da eşleşmeli ve KANONİK 'İpek' gitmeli.
        var leaf = new N11LeafAttributes("1", "Kumaş", new List<N11AttributeDef>
        {
            new("1", "Kumaş Tipi", IsMandatory: true, IsVariant: true, IsCustomValue: false, Priority: null,
                Values: new List<N11AttributeValue> { new("10", "İpek"), new("11", "Pamuk") }),
        });

        var result = _validator.Validate(
            leaf,
            new List<SalesChannelTrN11ProductCategoryAttribute>(),
            new List<N11SkuPushCandidate> { Candidate("V1", ("Kumaş Tipi", "İPEK")), Candidate("V2", ("Kumaş Tipi", "pamuk")) });

        result.VariantOptions.Values.SelectMany(x => x).Select(p => p.Value).ShouldBe(new[] { "İpek", "Pamuk" });
    }

    [Fact]
    public void Should_Allow_Single_Variant_Without_Options_When_No_Mandatory_Axis()
    {
        // Tek varyant + eksensiz kategori (Külçe Altın senaryosu) → seçeneksiz stockItem meşru.
        var leaf = new N11LeafAttributes("1", "Külçe Altın", new List<N11AttributeDef>());

        var result = _validator.Validate(
            leaf,
            new List<SalesChannelTrN11ProductCategoryAttribute>(),
            new List<N11SkuPushCandidate> { Candidate("V1") });

        result.VariantOptions.Single().Value.ShouldBeEmpty();
    }
}
