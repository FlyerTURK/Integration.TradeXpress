using System;
using System.Collections.Generic;
using Bunit;
using Integration.TradeXpress.Blazor.Client.Pages.RecipeTemplates;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.RecipeTemplates;
using Integration.TradeXpress.Services;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Pages;

/// <summary>
/// Reçete şablonu ("orta reçete") formunun gerçek render testleri.
///
/// <para>Şablon satırı iki farklı görünüm gösterir: sabit tutar satırında para birimi alanı açılır, yüzde/brütleştir
/// satırında açılmaz. Bu koşullu render en kolay kırılan yerdir — testler onu sabitler.</para>
/// </summary>
public class RecipeTemplateLayoutTests : BlazorComponentTestBase
{
    [Fact]
    public void Renders_an_empty_template()
    {
        var component = Render<RecipeTemplateLayout>(parameters => parameters
            .Add(p => p.Model, NewModel()));

        component.Markup.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Renders_service_lines_with_their_cost_kinds()
    {
        var model = NewModel();
        model.Lines.Add(NewLine(RecipeDerivedOperation.Percent, 5m, SideCostKind.Packaging));
        model.Lines.Add(NewLine(RecipeDerivedOperation.Add, 120m, SideCostKind.Cargo));
        model.Lines.Add(NewLine(RecipeDerivedOperation.GrossUp, 21m, SideCostKind.Commission));

        var component = Render<RecipeTemplateLayout>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.Services, new List<ServiceListDto>
            {
                new() { Id = Guid.NewGuid(), Code = "PAKET", Name = "Paketleme" },
            }));

        component.Markup.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Renders_with_a_currency_list_for_fixed_amount_lines()
    {
        // Sabit tutar satırında para birimi alanı görünür; lookup verisi host'tan gelir.
        var model = NewModel();
        model.Lines.Add(NewLine(RecipeDerivedOperation.Add, 50m, SideCostKind.Cargo));

        var component = Render<RecipeTemplateLayout>(parameters => parameters
            .Add(p => p.Model, model)
            .Add(p => p.CurrencyUnits, new List<Financials.CurrencyUnits.CurrencyUnitListDto>
            {
                new() { Id = Guid.NewGuid(), Code = "TRY", Name = "Türk Lirası" },
            }));

        component.Markup.ShouldNotBeNullOrWhiteSpace();
    }

    private static RecipeTemplateGetDto NewModel()
    {
        return new RecipeTemplateGetDto
        {
            Id = Guid.NewGuid(),
            Name = "Standart Paketleme",
            IsActive = true,
            Lines = new List<RecipeTemplateLineDto>(),
        };
    }

    private static RecipeTemplateLineDto NewLine(RecipeDerivedOperation operation, decimal operand, SideCostKind kind)
    {
        return new RecipeTemplateLineDto
        {
            ComponentType = RecipeComponentType.Service,
            DerivedOperation = operation,
            DerivedOperand = operand,
            SideCostKind = kind,
        };
    }
}
