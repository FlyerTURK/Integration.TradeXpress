using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// <see cref="ProductCategoryAttributeMerger"/> mekanik ağı (DB'siz).
///
/// <para><b>Neden bu testler var:</b> nitelik ve değer kimlikleri pazaryeri eşleştirmesinin hedefidir. Merge
/// yerine "hepsini yeniden yarat" davranışına dönülürse kimlikler her kaydetmede değişir ve tüm eşleştirmeler
/// SESSİZCE kopar — hiçbir hata da alınmaz. Aşağıdaki ilk test tam olarak bunu yakalar.</para>
/// </summary>
public class ProductCategoryAttributeMergerTests
{
    private static readonly Guid CompanyId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Existing_attribute_and_value_keep_their_ids_across_updates()
    {
        var category = BuildSavedCategory(("Ayar", new[] { "14K", "18K" }));
        var attributeId = category.Attributes.Single().Id;
        var valueId = category.Attributes.Single().Values.Single(v => v.Value == "14K").Id;

        // Kullanıcı adı değiştirip bir değer ekliyor — kimlikler AYNI kalmalı.
        ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new()
            {
                Id = attributeId,
                Name = "Ayar (Karat)",
                Values = new List<ProductCategoryAttributeValueDto>
                {
                    new() { Id = valueId, Value = "14K" },
                    new() { Id = category.Attributes.Single().Values.Single(v => v.Value == "18K").Id, Value = "18K" },
                    new() { Value = "22K" },   // yeni: Id boş
                },
            },
        });

        var attribute = category.Attributes.ShouldHaveSingleItem();
        attribute.Id.ShouldBe(attributeId);
        attribute.Name.ShouldBe("Ayar (Karat)");
        attribute.Values.Count.ShouldBe(3);
        attribute.Values.Single(v => v.Value == "14K").Id.ShouldBe(valueId);
        attribute.Values.Single(v => v.Value == "22K").Id.ShouldBe(Guid.Empty);   // yeni satır — Id'yi ABP verecek
    }

    [Fact]
    public void Attribute_missing_from_the_payload_is_removed()
    {
        var category = BuildSavedCategory(("Ayar", new[] { "14K" }), ("Renk", new[] { "Sarı" }));
        var keep = category.Attributes.Single(a => a.Name == "Ayar");

        ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new()
            {
                Id = keep.Id,
                Name = keep.Name,
                Values = keep.Values.Select(v => new ProductCategoryAttributeValueDto { Id = v.Id, Value = v.Value }).ToList(),
            },
        });

        category.Attributes.ShouldHaveSingleItem().Name.ShouldBe("Ayar");
    }

    [Fact]
    public void Value_missing_from_the_payload_is_removed()
    {
        var category = BuildSavedCategory(("Ayar", new[] { "14K", "18K" }));
        var attribute = category.Attributes.Single();
        var kept = attribute.Values.Single(v => v.Value == "14K");

        ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new()
            {
                Id = attribute.Id,
                Name = attribute.Name,
                Values = new List<ProductCategoryAttributeValueDto> { new() { Id = kept.Id, Value = kept.Value } },
            },
        });

        category.Attributes.Single().Values.ShouldHaveSingleItem().Value.ShouldBe("14K");
    }

    [Fact]
    public void Unknown_id_creates_a_new_row_instead_of_hijacking_another_categorys_attribute()
    {
        // Başka kategoriye ait bir Id gönderilirse o satır BURAYA çekilmemeli; yeni satır açılmalı.
        var category = BuildSavedCategory();
        var foreignId = Guid.NewGuid();

        ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new() { Id = foreignId, Name = "Kaçak" },
        });

        var attribute = category.Attributes.ShouldHaveSingleItem();
        attribute.Id.ShouldNotBe(foreignId);
        attribute.Id.ShouldBe(Guid.Empty);
        attribute.Name.ShouldBe("Kaçak");
    }

    [Fact]
    public void Blank_rows_are_dropped_instead_of_failing()
    {
        // Grid'de kullanıcı boş satır bırakmış olabilir — kaydetmeyi düşürmek yerine elenir.
        var category = BuildSavedCategory();

        ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new() { Name = "   " },
            new()
            {
                Name = "Renk",
                Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "" }, new() { Value = "Sarı" } },
            },
        });

        var attribute = category.Attributes.ShouldHaveSingleItem();
        attribute.Name.ShouldBe("Renk");
        attribute.Values.ShouldHaveSingleItem().Value.ShouldBe("Sarı");
    }

    [Fact]
    public void Duplicate_attribute_name_is_rejected_before_it_reaches_the_database()
    {
        // DB'de (CategoryId, Name) UNIQUE index var. Ön-kontrol olmasaydı kullanıcı bunu ham SQL çakışması
        // — anlaşılmaz genel hata — olarak görürdü (CLAUDE.md: "dostane BusinessException, ham DB değil").
        var category = BuildSavedCategory();

        Should.Throw<BusinessException>(() => ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new() { Name = "Renk" },
            new() { Name = "Renk" },
        })).Code.ShouldBe("TradeXpress:ProductCategory:AttributeNameAlreadyExists");
    }

    [Fact]
    public void Duplicate_attribute_name_check_ignores_letter_case()
    {
        // Kalıtım birleştirmesi nitelikleri OrdinalIgnoreCase eşleştirir (MergeAttributes) → "Renk" ile "RENK"
        // sistemde ZATEN tek niteliktir. İzin verilseydi kalıtımda tekleşen ama DB'de iki satır olan
        // tutarsız bir durum doğardı; bu test o kararı DB collation'ından bağımsız olarak sabitler.
        var category = BuildSavedCategory();

        Should.Throw<BusinessException>(() => ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new() { Name = "Renk" },
            new() { Name = "RENK" },
        })).Code.ShouldBe("TradeXpress:ProductCategory:AttributeNameAlreadyExists");
    }

    [Fact]
    public void Duplicate_value_under_one_attribute_is_rejected()
    {
        var category = BuildSavedCategory();

        Should.Throw<BusinessException>(() => ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new()
            {
                Name = "Ayar",
                Values = new List<ProductCategoryAttributeValueDto>
                {
                    new() { Value = "14K" },
                    new() { Value = "14k" },
                },
            },
        })).Code.ShouldBe("TradeXpress:ProductCategory:AttributeValueAlreadyExists");
    }

    [Fact]
    public void Renaming_an_attribute_onto_a_sibling_name_is_rejected()
    {
        // Sinsi yol: iki satır yeni DEĞİL, biri var olanın adı diğerininkine ÇEVRİLİYOR. Kontrol yalnız
        // "yeni satır" üzerinde olsaydı bu kaçar ve DB'ye ham çakışma olarak inerdi.
        var category = BuildSavedCategory(("Ayar", new[] { "14K" }), ("Renk", new[] { "Sarı" }));
        var ayar = category.Attributes.Single(a => a.Name == "Ayar");
        var renk = category.Attributes.Single(a => a.Name == "Renk");

        Should.Throw<BusinessException>(() => ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new() { Id = ayar.Id, Name = "Ayar" },
            new() { Id = renk.Id, Name = "Ayar" },   // "Renk" → "Ayar" olarak yeniden adlandırılıyor
        })).Code.ShouldBe("TradeXpress:ProductCategory:AttributeNameAlreadyExists");
    }

    [Fact]
    public void Same_value_under_different_attributes_is_allowed()
    {
        // Benzersizlik NİTELİK başınadır: "Sarı" hem Renk'te hem Kaplama'da bulunabilir. Fazla kısıtlamak
        // meşru veriyi engellerdi (DB index'i de (AttributeId, Value) üzerinde, salt Value değil).
        var category = BuildSavedCategory();

        ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new() { Name = "Renk", Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "Sarı" } } },
            new() { Name = "Kaplama", Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "Sarı" } } },
        });

        category.Attributes.Count.ShouldBe(2);
        category.Attributes.ShouldAllBe(a => a.Values.Count == 1);
    }

    [Fact]
    public void An_inherited_attribute_is_not_persisted_into_this_category()
    {
        // Grid devralınanları da gösterir (2026-07-28 Hakan). Kaydetmede bunlar YOK SAYILIR — kopyalansaydı
        // üst kategorinin nitelikleri her alt kategoriye çoğalır ve kalıtım anlamsızlaşırdı.
        var category = BuildSavedCategory();

        ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new()
            {
                Name = "Ayar",
                IsInherited = true,
                SourceCategoryName = "Takı",
                Values = new List<ProductCategoryAttributeValueDto>
                {
                    new() { Value = "14K", IsInherited = true },
                    new() { Value = "18K", IsInherited = true },
                },
            },
        });

        category.Attributes.ShouldBeEmpty();
    }

    [Fact]
    public void Adding_an_own_value_under_an_inherited_attribute_creates_a_shadow_attribute()
    {
        // Kullanıcı devralınan "Ayar"a 22K ekliyor. Bu kategoride "Ayar" açılır ve YALNIZ 22K yazılır;
        // devralınan 14K/18K kopyalanmaz (onlar üst kategoride yaşar, kalıtım birleştirmesi yine gösterir).
        var category = BuildSavedCategory();

        ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new()
            {
                Name = "Ayar",
                Kind = ProductCategoryAttributeKind.Specification,
                IsInherited = true,
                SourceCategoryName = "Takı",
                Values = new List<ProductCategoryAttributeValueDto>
                {
                    new() { Value = "14K", IsInherited = true },
                    new() { Value = "18K", IsInherited = true },
                    new() { Value = "22K" },   // kullanıcının eklediği
                },
            },
        });

        var attribute = category.Attributes.ShouldHaveSingleItem();
        attribute.Name.ShouldBe("Ayar");
        attribute.Values.ShouldHaveSingleItem().Value.ShouldBe("22K");
    }

    [Fact]
    public void An_inherited_attribute_id_is_never_used_as_a_merge_key()
    {
        // Devralınan satırın Id'si ÜST kategorinin niteliğine aittir. Merge anahtarı sayılsaydı alt kategoride
        // yapılan düzenleme üstteki niteliği değiştirirdi — kalıtımın sessizce kırılması.
        var category = BuildSavedCategory(("Ayar", new[] { "14K" }));
        var ownAttributeId = category.Attributes.Single().Id;
        var foreignId = Guid.NewGuid();

        ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new()
            {
                Id = ownAttributeId,
                Name = "Ayar",
                Values = new List<ProductCategoryAttributeValueDto>
                {
                    new() { Id = category.Attributes.Single().Values.Single().Id, Value = "14K" },
                },
            },
            new()
            {
                Id = foreignId,                     // üst kategorinin nitelik Id'si
                Name = "Renk",
                IsInherited = true,
                Values = new List<ProductCategoryAttributeValueDto> { new() { Value = "Sarı" } },
            },
        });

        var shadow = category.Attributes.Single(a => a.Name == "Renk");
        shadow.Id.ShouldNotBe(foreignId);   // yeni satır açıldı, üstteki ele geçirilmedi
        category.Attributes.Single(a => a.Name == "Ayar").Id.ShouldBe(ownAttributeId);
    }

    [Fact]
    public void Inherited_values_are_never_copied_into_an_own_attribute()
    {
        // Nitelik KENDİ olsa bile değerlerinin bir kısmı üstten devralınmış görünebilir (ekleyerek birleşme).
        // Devralınan değer buraya kopyalanırsa aynı değer iki kategoride durur ve üstteki düzeltilince
        // alttaki bayat kalırdı.
        var category = BuildSavedCategory(("Ayar", new[] { "22K" }));
        var attribute = category.Attributes.Single();

        ProductCategoryAttributeMerger.Apply(category, new List<ProductCategoryAttributeDto>
        {
            new()
            {
                Id = attribute.Id,
                Name = "Ayar",
                Values = new List<ProductCategoryAttributeValueDto>
                {
                    new() { Value = "14K", IsInherited = true },   // üstten
                    new() { Id = attribute.Values.Single().Id, Value = "22K" },
                },
            },
        });

        category.Attributes.Single().Values.ShouldHaveSingleItem().Value.ShouldBe("22K");
    }

    [Fact]
    public void Null_payload_clears_every_attribute()
    {
        var category = BuildSavedCategory(("Ayar", new[] { "14K" }));

        ProductCategoryAttributeMerger.Apply(category, null);

        category.Attributes.ShouldBeEmpty();
    }

    /// <summary>Kaydedilmiş (kimlikleri dolu) bir kategori kurar — DB'den yüklenmiş hâli taklit eder.</summary>
    private static ProductCategory BuildSavedCategory(params (string Name, string[] Values)[] attributes)
    {
        var category = new ProductCategory(CompanyId, "Takı");
        SetId(category);

        foreach (var (name, values) in attributes)
        {
            var attribute = category.AddAttribute(name);
            SetId(attribute);

            foreach (var value in values)
            {
                SetId(attribute.AddValue(value));
            }
        }

        return category;
    }

    // ABP kaydetmede Id'yi kendisi atar; bellek-içi testte kimlik korunumunu sınamak için elle veriyoruz.
    private static void SetId<TEntity>(TEntity entity)
        where TEntity : IEntity<Guid>
    {
        EntityHelper.TrySetId(entity, () => Guid.NewGuid(), checkForDisableIdGenerationAttribute: false);
    }
}
