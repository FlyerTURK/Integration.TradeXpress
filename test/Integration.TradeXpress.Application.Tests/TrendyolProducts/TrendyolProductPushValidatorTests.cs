using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.TrendyolCategories;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// TRENDYOL PUSH ÖN-KONTROLÜ — <see cref="TrendyolProductPushValidator"/> (N11 doğrulayıcısının id-bazlı portu).
///
/// <para><b>Çivilenen delik:</b> gerçek push kategori tanımına HİÇ bakmıyordu — eksik zorunlu attribute, listede
/// olmayan eksen değeri, aynı imzalı iki varyant… hepsi Trendyol'a gidip saatler sonra batch reddi olarak
/// dönüyordu. Bu ağ, ön-kontrolün yedi kuralını ve foto-önceliğini (import fotoğrafı kategori eşleştirmesine
/// girmez — pazaryerinin beyanı) pinler.</para>
/// </summary>
public class TrendyolProductPushValidatorTests
{
    private readonly TrendyolProductPushValidator _validator = new();

    private static TrendyolLeafAttributeDto Def(
        int id, string name, bool required = false, bool varianter = false, bool allowCustom = false,
        params (int ValueId, string Value)[] values)
    {
        return new TrendyolLeafAttributeDto
        {
            AttributeId = id,
            Name = name,
            Required = required,
            Varianter = varianter,
            AllowCustom = allowCustom,
            Values = values.Select(v => new TrendyolAttributeValueDto { ValueId = v.ValueId, Value = v.Value }).ToList(),
        };
    }

    private static TrendyolPushVariantInput Erp(Guid id, string code, params (string Name, string Value)[] options)
    {
        return new TrendyolPushVariantInput(
            id, code, options.ToList(), Array.Empty<SalesChannelTrTrendyolProductSkuRemoteAxisValue>());
    }

    private static readonly List<TrendyolLeafAttributeDto> ColorCategory = new()
    {
        Def(47, "Renk", required: true, varianter: true, values: ((int, string)[])new[] { (686234, "Kırmızı"), (686240, "Mavi") }),
        Def(60, "Materyal", required: true, values: ((int, string)[])new[] { (1001, "Deri") }),
    };

    private static List<SalesChannelTrTrendyolProductCategoryAttribute> MaterialFilled()
    {
        return new List<SalesChannelTrTrendyolProductCategoryAttribute>
        {
            new(60, 1001, null),
        };
    }

    [Fact]
    public void Erp_option_resolves_to_canonical_listed_value_id()
    {
        var a = Guid.NewGuid();
        var result = _validator.Validate(ColorCategory, MaterialFilled(), new[]
        {
            // tr-TR IgnoreCase: "KIRMIZI" (noktasız I) listedeki "Kırmızı" ile eşleşir. ASCII "kirmizi"
            // EŞLEŞMEZ — noktalı i ile noktasız ı Türkçede ayrı harflerdir (bilinçli davranış, N11 paritesi).
            Erp(a, "VAR-A", ("renk", "KIRMIZI")),
        });

        var axis = result.VariantAxes[a];
        axis.Attributes.ShouldHaveSingleItem().AttributeValueId.ShouldBe(686234);
        axis.Options.ShouldHaveSingleItem().ShouldBe(("Renk", "Kırmızı"));   // kanonik yazım LİSTEDEN döner
        axis.Signature.ShouldHaveSingleItem().AttributeValueId.ShouldBe(686234);
    }

    [Fact]
    public void Photo_values_bypass_category_matching()
    {
        var a = Guid.NewGuid();
        var photo = new TrendyolPushVariantInput(a, "VAR-A",
            Array.Empty<(string, string)>(),
            new List<SalesChannelTrTrendyolProductSkuRemoteAxisValue>
            {
                // Kategori tanımında OLMAYAN id — pazaryerinin beyanı aynen geçer, eşleştirme aranmaz.
                new(999, 12345, "Gizemli", "Bilinmeyen Eksen"),
            });

        var result = _validator.Validate(ColorCategory, MaterialFilled(), new[] { photo });

        var axis = result.VariantAxes[a];
        axis.Attributes.ShouldHaveSingleItem().AttributeId.ShouldBe(999);
        axis.Options.ShouldHaveSingleItem().ShouldBe(("Bilinmeyen Eksen", "Gizemli"));
    }

    [Fact]
    public void Multi_variant_product_needs_a_variant_axis_in_the_category()
    {
        var defs = new List<TrendyolLeafAttributeDto> { Def(60, "Materyal") };   // varianter YOK

        Should.Throw<BusinessException>(() => _validator.Validate(defs, new List<SalesChannelTrTrendyolProductCategoryAttribute>(), new[]
        {
            Erp(Guid.NewGuid(), "A"),
            Erp(Guid.NewGuid(), "B"),
        })).Code.ShouldBe("TradeXpress:Trendyol:Product:CategoryHasNoVariantAxis");
    }

    [Fact]
    public void Erp_axis_not_in_varianter_set_fails_fast()
    {
        Should.Throw<BusinessException>(() => _validator.Validate(ColorCategory, MaterialFilled(), new[]
        {
            Erp(Guid.NewGuid(), "A", ("Beden", "M")),
        })).Code.ShouldBe("TradeXpress:Trendyol:Product:VariantAxisNotAllowed");
    }

    [Fact]
    public void Value_outside_list_without_custom_permission_fails_fast()
    {
        Should.Throw<BusinessException>(() => _validator.Validate(ColorCategory, MaterialFilled(), new[]
        {
            Erp(Guid.NewGuid(), "A", ("Renk", "Turkuaz")),
        })).Code.ShouldBe("TradeXpress:Trendyol:Product:AttributeValueNotInList");
    }

    [Fact]
    public void Custom_permission_carries_free_text_without_value_id()
    {
        var defs = new List<TrendyolLeafAttributeDto>
        {
            Def(75, "Hacim", varianter: true, allowCustom: true),
        };
        var a = Guid.NewGuid();

        var result = _validator.Validate(defs, new List<SalesChannelTrTrendyolProductCategoryAttribute>(), new[]
        {
            Erp(a, "A", ("Hacim", "50 ml")),
        });

        var attribute = result.VariantAxes[a].Attributes.ShouldHaveSingleItem();
        attribute.AttributeValueId.ShouldBeNull();
        attribute.CustomValue.ShouldBe("50 ml");
        result.VariantAxes[a].Signature.ShouldBeEmpty();   // serbest metnin valueId'si yok → imzaya girmez
    }

    [Fact]
    public void Mandatory_varianter_axis_must_exist_on_every_variant()
    {
        Should.Throw<BusinessException>(() => _validator.Validate(ColorCategory, MaterialFilled(), new[]
        {
            Erp(Guid.NewGuid(), "A", ("Renk", "Kırmızı")),
            Erp(Guid.NewGuid(), "B"),   // Renk YOK
        })).Code.ShouldBe("TradeXpress:Trendyol:Product:VariantAxisMissing");
    }

    [Fact]
    public void Two_variants_with_identical_signatures_are_rejected()
    {
        Should.Throw<BusinessException>(() => _validator.Validate(ColorCategory, MaterialFilled(), new[]
        {
            Erp(Guid.NewGuid(), "A", ("Renk", "Kırmızı")),
            Erp(Guid.NewGuid(), "B", ("Renk", "KIRMIZI")),   // kanonikte AYNI değer (tr-TR katlama)
        })).Code.ShouldBe("TradeXpress:Trendyol:Product:DuplicateVariantSignature");
    }

    [Fact]
    public void Missing_mandatory_product_attribute_fails_fast()
    {
        Should.Throw<BusinessException>(() => _validator.Validate(ColorCategory, new List<SalesChannelTrTrendyolProductCategoryAttribute>(), new[]
        {
            Erp(Guid.NewGuid(), "A", ("Renk", "Kırmızı")),
        })).Code.ShouldBe("TradeXpress:Trendyol:Product:ProductAttributeMissing");
    }

    [Fact]
    public void Varianter_product_level_attribute_is_filtered_from_product_output()
    {
        var a = Guid.NewGuid();
        var productAttributes = MaterialFilled();
        productAttributes.Add(new SalesChannelTrTrendyolProductCategoryAttribute(47, 686234, null));   // varianter — ürün seviyesine sızmış

        var result = _validator.Validate(ColorCategory, productAttributes, new[]
        {
            Erp(a, "A", ("Renk", "Mavi")),
        });

        result.ProductAttributes.ShouldHaveSingleItem().AttributeId.ShouldBe(60);   // Renk elendi, kalemle gider
        result.VariantAxes[a].Attributes.ShouldHaveSingleItem().AttributeValueId.ShouldBe(686240);   // kalemin KENDİ değeri öncelikli
    }

    /// <summary>
    /// TEK KALEMLİ İMPORT VAKASI (bağımsız denetim bulgusu, 2026-08-14): tek kalemli grupta eksen çıkarılamaz,
    /// import "Renk=Kırmızı"yı ÜRÜN seviyesine yazar ve SKU'nun RemoteVariantAttributes'ı BOŞ kalır. İlk sürüm ürün-seviyesi
    /// varianter değeri yalnız ELİYORDU — kaleme taşımadan; sonuç: eksen push'tan tamamen düşüyor, zorunlu-
    /// varianter kategoride Trendyol kesin reddediyordu. Doğru davranış: ürün-seviyesi varianter değer HER
    /// kaleme DEVREDİLİR (aynı ürünün tek değeri — çelişki üretmez).
    /// </summary>
    [Fact]
    public void Product_level_varianter_value_is_carried_onto_items_when_the_item_has_none()
    {
        var a = Guid.NewGuid();
        var productAttributes = MaterialFilled();
        productAttributes.Add(new SalesChannelTrTrendyolProductCategoryAttribute(47, 686234, null));   // Renk=Kırmızı ürün seviyesinde

        // Kalemin ne ERP çifti ne RemoteVariantAttributes'ı var (tek kalemli import böyle gelir).
        var result = _validator.Validate(ColorCategory, productAttributes, new[] { Erp(a, "A") });

        result.ProductAttributes.ShouldHaveSingleItem().AttributeId.ShouldBe(60);           // ürün seviyesinden yine elenir
        var axis = result.VariantAxes[a];
        axis.Attributes.ShouldHaveSingleItem().AttributeValueId.ShouldBe(686234);            // ama kaleme TAŞINDI
        axis.Options.ShouldHaveSingleItem().ShouldBe(("Renk", "Kırmızı"));                    // PushHistory'nin okunur çifti
        axis.Signature.ShouldHaveSingleItem().AttributeValueId.ShouldBe(686234);             // yeniden-bağlama imzası da dolu
    }

    /// <summary>Kalemler FARKLI eksen kümesi taşıyamaz — biri yalnız Renk, diğeri Renk+Beden ise Trendyol
    /// grubu tutarsız sayar. (Bu kod eksik testti — 7 kuralın 6'sı pinliydi; denetim bulgusu.)</summary>
    [Fact]
    public void Inconsistent_axis_sets_across_variants_are_rejected()
    {
        var category = new List<TrendyolLeafAttributeDto>
        {
            Def(47, "Renk", required: true, varianter: true, values: ((int, string)[])new[] { (686234, "Kırmızı"), (686240, "Mavi") }),
            Def(48, "Beden", varianter: true, values: ((int, string)[])new[] { (1, "S"), (2, "M") }),
        };

        Should.Throw<BusinessException>(() => _validator.Validate(category, new List<SalesChannelTrTrendyolProductCategoryAttribute>(), new[]
        {
            Erp(Guid.NewGuid(), "A", ("Renk", "Kırmızı")),
            Erp(Guid.NewGuid(), "B", ("Renk", "Mavi"), ("Beden", "M")),
        })).Code.ShouldBe("TradeXpress:Trendyol:Product:VariantAttributesInconsistent");
    }

    /// <summary>Zorunlu ürün-seviyesi attribute KAYDI VAR ama değeri BOŞ (ne id ne metin) → yine eksik sayılır;
    /// CustomValue dolu kayıt zorunluluğu karşılar. (Denetim bulgusu: iki dal assert'sizdi.)</summary>
    [Fact]
    public void Present_but_empty_mandatory_product_attribute_is_still_missing_and_custom_text_satisfies_it()
    {
        var empty = new List<SalesChannelTrTrendyolProductCategoryAttribute> { new(60, null, "   ") };
        Should.Throw<BusinessException>(() => _validator.Validate(ColorCategory, empty, new[]
        {
            Erp(Guid.NewGuid(), "A", ("Renk", "Kırmızı")),
        })).Code.ShouldBe("TradeXpress:Trendyol:Product:ProductAttributeMissing");

        var customCategory = new List<TrendyolLeafAttributeDto>
        {
            Def(47, "Renk", required: true, varianter: true, values: ((int, string)[])new[] { (686234, "Kırmızı") }),
            Def(60, "Materyal", required: true, allowCustom: true),
        };
        var custom = new List<SalesChannelTrTrendyolProductCategoryAttribute> { new(60, null, "Vegan Deri") };
        var result = _validator.Validate(customCategory, custom, new[] { Erp(Guid.NewGuid(), "A", ("Renk", "Kırmızı")) });
        result.ProductAttributes.ShouldHaveSingleItem().CustomValue.ShouldBe("Vegan Deri");
    }
}
