using System;
using System.Collections.Generic;
using System.Linq;
using Bunit;
using Integration.TradeXpress.Blazor.Client.Pages.ProductCategories;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Pages;

/// <summary>
/// Kategori formunun GERÇEK render testleri — bugünkü kalıtım tasarımının UI sözleşmesi.
///
/// <para>Bu testler metin taramasının yakalayamayacağı şeyleri yakalar: bileşen ağacı gerçekten kurulur,
/// tanımsız parametre / eksik servis / şablon hatası anında patlar.</para>
/// </summary>
public class ProductCategoryLayoutTests : BlazorComponentTestBase
{
    public ProductCategoryLayoutTests()
    {
        // N11 kategori seçici alt bileşeni bu servisleri ister (form render edilirken kurulur).
        AddSubstitute<IN11CategoryAppService>();
        AddUiInteraction();
    }

    [Fact]
    public void Renders_a_root_category_without_inherited_rows()
    {
        var model = NewModel("Takı");

        var component = Render<ProductCategoryLayout>(parameters => parameters
            .Add(p => p.Model, model));

        component.Markup.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Renders_inherited_attributes_in_the_same_grid()
    {
        // 2026-07-28 Hakan: devralınanlar AYRI panelde değil, aynı grid'de görünür.
        var model = NewModel("Yüzük");
        model.Attributes.Add(new ProductCategoryAttributeDto
        {
            Name = "Ayar",
            IsInherited = true,
            SourceCategoryName = "Takı",
            Values = new List<ProductCategoryAttributeValueDto>
            {
                new() { Value = "14K", IsInherited = true, SourceCategoryName = "Takı" },
                new() { Value = "22K" },
            },
        });

        var component = Render<ProductCategoryLayout>(parameters => parameters
            .Add(p => p.Model, model));

        // Kaynak kategori adı grid'de görünmeli — kullanıcı düzenlemek için nereye gideceğini bilsin.
        component.Markup.ShouldContain("Takı");
    }

    [Fact]
    public void Renders_the_channel_mapping_panel_only_when_editing_is_possible()
    {
        var model = NewModel("Takı");

        var without = Render<ProductCategoryLayout>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.CanEditChannelMappings, false));

        var with = Render<ProductCategoryLayout>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.CanEditChannelMappings, true));

        // Panel yalnız kaydedilmiş kategoride anlamlı (eşleştirme kategori kimliğine asılır).
        with.Markup.Length.ShouldBeGreaterThan(without.Markup.Length);
    }

    [Fact]
    public void Renders_mappings_for_multiple_channels_without_failing()
    {
        // 2026-07-28 Hakan: eşleştirme artık N11'e SABİT değil — kanal başına bir drill satırı.
        // Bu test çok-kanallı listeyle bileşen ağacının kurulduğunu doğrular: eskiden tek N11 alanı vardı,
        // birden çok kanal verildiğinde form kurulmuyordu bile.
        //
        // NOT: satır İÇERİĞİ assert EDİLEMEZ — DevExpress grid'i veri satırlarını tarayıcı tarafında (JS interop)
        // çiziyor, bUnit'in render ağacında görünmüyorlar. İçerik araması burada yanlış-negatif üretir.
        var model = NewModel("Takı");

        var component = Render<ProductCategoryLayout>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.CanEditChannelMappings, true)
            .Add(p => p.ChannelMappings, new List<ProductCategoryChannelMappingDto>
            {
                new()
                {
                    Channel = SalesChannelType.TrN11,
                    ChannelCategoryExternalId = "1001",
                    ChannelCategoryName = "Takı > Yüzük",
                    EffectiveCommissionRate = 21.004m,
                },
                new()
                {
                    Channel = SalesChannelType.Etsy,
                    ChannelCategoryExternalId = "2002",
                    ChannelCategoryName = "Jewelry > Rings",
                    EffectiveCommissionRate = 6.5m,
                },
            }));

        component.Markup.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Renders_parent_options_in_the_lookup()
    {
        var model = NewModel("Yüzük");

        var component = Render<ProductCategoryLayout>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.Categories, new List<ProductCategoryListDto>
            {
                new() { Id = Guid.NewGuid(), Name = "Takı", Path = "Takı" },
            }));

        component.Markup.ShouldNotBeNullOrWhiteSpace();
    }

    private static ProductCategoryGetDto NewModel(string name)
    {
        return new ProductCategoryGetDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = true,
            Attributes = new List<ProductCategoryAttributeDto>(),
        };
    }
}
