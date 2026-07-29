using System;
using System.Collections.Generic;
using Bunit;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Substitutions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Components;

/// <summary>
/// Muadil "Varyant Kapsamı" ağacının GERÇEK render testi.
///
/// <para><b>Neden var:</b> 2026-07-28'de "Tümünü Seç" butonunu grup başlığına taşırken ağaç sessizce
/// kayboldu — derleme geçti, emtialar ekranda yoktu. Bu test o sınıf hatayı yakalar: panel veriyle
/// beslendiğinde maden kodlarının markup'a GERÇEKTEN düşmesini şart koşar.</para>
///
/// <para>Ayrıca varsayılan kapsamı sabitler: kalem kendi listesini doldurmamışsa madenin TÜM varyantları
/// seçili gelir (kullanıcı istemediğini çıkarır) — eskiden yalnız ana varyant devralınıyordu.</para>
/// </summary>
public class SubstitutionVariantScopeRenderTests : BlazorComponentTestBase
{
    private static readonly Guid MetalId = Guid.NewGuid();
    private static readonly Guid MainVariantId = Guid.NewGuid();
    private static readonly Guid SecondVariantId = Guid.NewGuid();

    [Fact]
    public void Renders_metal_nodes_with_their_codes()
    {
        var component = RenderPanel(new List<Guid>());

        // Maden kodu görünmüyorsa ağaç çizilmemiş demektir (başlık şablonu regresyonunun imzası).
        component.Markup.ShouldContain("G5.0GR995");
    }

    [Fact]
    public void Defaults_to_all_variants_when_the_group_item_has_no_explicit_scope()
    {
        var scope = new List<Guid>();

        RenderPanel(scope);

        // Override modunda panel açılışta grubun devralınan kapsamını ürünün listesine kopyalar; kalem
        // kendi kapsamını belirtmediğinde bu kapsam madenin TÜM varyantlarıdır.
        scope.ShouldContain(MainVariantId);
        scope.ShouldContain(SecondVariantId);
    }

    [Fact]
    public void Product_form_renders_the_scope_tree_inside_its_group()
    {
        // Grubun BAŞLIK ŞABLONU (HeaderContentTemplate) kullanıldığında gövdenin de çizilmeye devam ettiğini
        // sabitler — ilk denemede tam burada kırılmıştı: derleme geçti, ağaç ekranda yoktu.
        AddSubstitute<IN11CategoryAppService>();
        AddUiInteraction();

        var component = Render<ProductLayout>(parameters => parameters
            .Add(p => p.Model, new ProductGetDto
            {
                Id = Guid.NewGuid(),
                Code = "TEST",
                Name = "Test",
                VariantMode = ProductVariantMode.Substitution,
                SubstitutionGroupId = Guid.NewGuid(),
            })
            .Add(p => p.SubstitutionGroupItems, new List<SubstitutionGroupItemGraphDto>
            {
                new() { MetalId = MetalId },
            })
            .Add(p => p.MetalVariants, new List<MetalVariantLookupDto>
            {
                new()
                {
                    CommodityId = MetalId, VariantId = MainVariantId, IsMain = true,
                    MetalCode = "G5.0GR995", VariantCode = "ANAVARYANT",
                },
            })
            .Add(p => p.Metals, new List<MetalListDto>
            {
                new() { Id = MetalId, Code = "G5.0GR995", StableQuantity = 5m },
            }));

        component.Markup.ShouldContain("G5.0GR995");
    }

    private IRenderedComponent<SubstitutionVariantTreePanel> RenderPanel(List<Guid> overrideVariantIds)
    {
        return Render<SubstitutionVariantTreePanel>(parameters => parameters
            .Add(p => p.Items, new List<SubstitutionGroupItemGraphDto>
            {
                new() { MetalId = MetalId },
            })
            .Add(p => p.Variants, new List<MetalVariantLookupDto>
            {
                new()
                {
                    CommodityId = MetalId, VariantId = MainVariantId, IsMain = true,
                    MetalCode = "G5.0GR995", VariantCode = "ANAVARYANT",
                },
                new()
                {
                    CommodityId = MetalId, VariantId = SecondVariantId, IsMain = false,
                    MetalCode = "G5.0GR995", VariantCode = "IKINCI",
                },
            })
            .Add(p => p.Metals, new List<MetalListDto>
            {
                new() { Id = MetalId, Code = "G5.0GR995", StableQuantity = 5m },
            })
            .Add(p => p.OverrideVariantIds, overrideVariantIds));
    }
}
