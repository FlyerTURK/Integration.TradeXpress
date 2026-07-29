using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Kategori KALITIMININ mekanik ağı (DB'siz — <see cref="ProductCategoryTreeManager.MergeAttributes"/> saf).
///
/// <para>Kural (2026-07-27 Hakan): "üst kategorinin attribute ve value'larını alt kategoriler inherit alsın."
/// Birleştirme EKLEMELİDİR (union), ezme değil — bu testler ezmeye dönülürse kırmızı yanar.</para>
/// </summary>
public class ProductCategoryInheritanceTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Child_inherits_parent_attributes()
    {
        var root = BuildCategory("Takı");
        var attribute = AddAttribute(root, "Materyal", ProductCategoryAttributeKind.Specification, "Altın", "Gümüş");
        var child = BuildCategory("Yüzük", root.Id);

        var effective = ProductCategoryTreeManager.MergeAttributes(new[] { root, child }, child.Id);

        var inherited = effective.ShouldHaveSingleItem();
        inherited.Name.ShouldBe("Materyal");
        inherited.IsInherited.ShouldBeTrue();
        inherited.SourceCategoryName.ShouldBe("Takı");
        // Kimlik SAHİBİNDEN gelir — kanal eşleştirmesi üst kategorinin satırına asılacak.
        inherited.AttributeId.ShouldBe(attribute.Id);
        inherited.Values.Select(v => v.Value).ShouldBe(new[] { "Altın", "Gümüş" });
        inherited.Values.ShouldAllBe(v => v.IsInherited);
    }

    [Fact]
    public void Own_attributes_are_not_marked_inherited()
    {
        var root = BuildCategory("Takı");
        var child = BuildCategory("Yüzük", root.Id);
        AddAttribute(child, "Ayar", ProductCategoryAttributeKind.Specification, "14K");

        var effective = ProductCategoryTreeManager.MergeAttributes(new[] { root, child }, child.Id);

        effective.ShouldHaveSingleItem().IsInherited.ShouldBeFalse();
    }

    [Fact]
    public void Same_named_attribute_merges_values_additively_instead_of_overriding()
    {
        // Üstte 14K/18K, altta 22K → etkin liste ÜÇÜ birden. Ezme olsaydı yalnız 22K kalırdı ve alt
        // kategoriye tek değer eklemek için üsttekileri tekrar yazmak gerekirdi.
        var root = BuildCategory("Takı");
        AddAttribute(root, "Ayar", ProductCategoryAttributeKind.Specification, "14K", "18K");
        var child = BuildCategory("Yüzük", root.Id);
        AddAttribute(child, "Ayar", ProductCategoryAttributeKind.Specification, "22K");

        var effective = ProductCategoryTreeManager.MergeAttributes(new[] { root, child }, child.Id);

        var merged = effective.ShouldHaveSingleItem();
        merged.Values.Select(v => v.Value).ShouldBe(new[] { "14K", "18K", "22K" });
        // Devralınan değerler kaynağını korur; kendi değeri "kendi" işaretlidir.
        merged.Values.Single(v => v.Value == "14K").IsInherited.ShouldBeTrue();
        merged.Values.Single(v => v.Value == "22K").IsInherited.ShouldBeFalse();
    }

    [Fact]
    public void Attribute_name_match_is_case_insensitive()
    {
        var root = BuildCategory("Takı");
        AddAttribute(root, "Ayar", ProductCategoryAttributeKind.Specification, "14K");
        var child = BuildCategory("Yüzük", root.Id);
        AddAttribute(child, "AYAR", ProductCategoryAttributeKind.Specification, "22K");

        var effective = ProductCategoryTreeManager.MergeAttributes(new[] { root, child }, child.Id);

        effective.ShouldHaveSingleItem().Values.Count.ShouldBe(2);
    }

    [Fact]
    public void Duplicate_value_is_listed_once_and_keeps_the_topmost_source()
    {
        var root = BuildCategory("Takı");
        AddAttribute(root, "Ayar", ProductCategoryAttributeKind.Specification, "14K");
        var child = BuildCategory("Yüzük", root.Id);
        AddAttribute(child, "Ayar", ProductCategoryAttributeKind.Specification, "14k");   // aynı değer, farklı yazım

        var effective = ProductCategoryTreeManager.MergeAttributes(new[] { root, child }, child.Id);

        var value = effective.ShouldHaveSingleItem().Values.ShouldHaveSingleItem();
        value.Value.ShouldBe("14K");                    // ilk (üstteki) yazım korunur
        value.IsInherited.ShouldBeTrue();               // kaynak da üsttedir
    }

    [Fact]
    public void Redefined_attribute_takes_kind_from_the_deepest_level()
    {
        // Üstte spesifikasyon, altta varyant ekseni → en DAR tanım kazanır (alt seviye).
        var root = BuildCategory("Takı");
        AddAttribute(root, "Renk", ProductCategoryAttributeKind.Specification, "Sarı");
        var child = BuildCategory("Yüzük", root.Id);
        AddAttribute(child, "Renk", ProductCategoryAttributeKind.VariantAxis, "Beyaz");

        var effective = ProductCategoryTreeManager.MergeAttributes(new[] { root, child }, child.Id);

        var merged = effective.ShouldHaveSingleItem();
        merged.Kind.ShouldBe(ProductCategoryAttributeKind.VariantAxis);
        merged.IsInherited.ShouldBeFalse();   // son tanım kendisine ait → burada düzenlenebilir
    }

    [Fact]
    public void Inheritance_flows_through_every_level_no_matter_how_deep()
    {
        // Derinlik TAVANI YOK (2026-07-27 kararı): 20 seviyelik zincirde kökün niteliği en alta ulaşmalı.
        var chain = new List<ProductCategory>();
        ProductCategory? parent = null;
        for (var level = 0; level < 20; level++)
        {
            var node = BuildCategory($"Seviye {level}", parent?.Id);
            chain.Add(node);
            parent = node;
        }

        AddAttribute(chain[0], "Materyal", ProductCategoryAttributeKind.Specification, "Altın");
        var leaf = chain[^1];

        var effective = ProductCategoryTreeManager.MergeAttributes(chain, leaf.Id);

        var inherited = effective.ShouldHaveSingleItem();
        inherited.IsInherited.ShouldBeTrue();
        inherited.SourceCategoryName.ShouldBe("Seviye 0");
    }

    private static ProductCategory BuildCategory(string name, Guid? parentId = null)
    {
        var category = new ProductCategory(CompanyId, name, parentId);
        SetId(category);
        return category;
    }

    private static ProductCategoryAttribute AddAttribute(
        ProductCategory category,
        string name,
        ProductCategoryAttributeKind kind,
        params string[] values)
    {
        var attribute = category.AddAttribute(name, kind);
        SetId(attribute);

        for (var index = 0; index < values.Length; index++)
        {
            SetId(attribute.AddValue(values[index], index));
        }

        return attribute;
    }

    // ABP kaydetmede Id'yi kendisi atar; bellek-içi testte kimlik davranışını sınamak için elle veriyoruz.
    private static void SetId<TEntity>(TEntity entity)
        where TEntity : IEntity<Guid>
    {
        EntityHelper.TrySetId(entity, () => Guid.NewGuid(), checkForDisableIdGenerationAttribute: false);
    }
}
