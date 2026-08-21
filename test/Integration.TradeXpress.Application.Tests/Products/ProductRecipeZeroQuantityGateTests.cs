using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// REÇETE GİRİŞ GUARD'I — "ana emtialar 0 adet veya miktar olarak girilmemeli" (2026-08-19 Hakan kuralı),
/// reçete yazımının TEK yolu olan <see cref="ProductRecipeLineWriter"/> üzerinden:
/// <list type="bullet">
///   <item>0 adet + 0 miktar katalog satırı REDDEDİLİR (kod pinli) ve HİÇBİR satır yazılmaz (fail-fast — kısmi
///   yazım yok; geçerli satır bile reddedilen satırla birlikte dışarıda kalır).</item>
///   <item>Yalnız adet &gt; 0 ya da yalnız miktar &gt; 0 KABUL edilir (adetli emtiada miktar türetilir, gramlı
///   emtiada adet boş kalabilir).</item>
///   <item>Hizmet satırı kapsam dışıdır — sıfırla yazılır.</item>
/// </list>
/// KIRMIZIYSA sıfır satırı reçeteye sızar (maliyet sıfırlanır, varyant "reçeteli" görünür ama reçete yoktur)
/// ya da guard hizmet satırını da vurur — testi gevşetme.
/// </summary>
public abstract class ProductRecipeZeroQuantityGateTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ProductRecipeLineWriter _writer;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLines;

    protected ProductRecipeZeroQuantityGateTests()
    {
        _writer = GetRequiredService<ProductRecipeLineWriter>();
        _recipeLines = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
    }

    [Fact]
    public async Task Catalog_line_with_zero_quantity_and_zero_amount_is_rejected_and_nothing_is_written()
    {
        var companyId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        // Geçerli bir satırın ARDINDAN sıfır satırı: guard ikisini birden dışarıda bırakmalı (kısmi yazım yok).
        var exception = await Should.ThrowAsync<BusinessException>(() => WithUnitOfWorkAsync(() =>
            _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
            {
                BuildMetalLine(order: 0, quantity: 1m, amount: 5m),
                BuildMetalLine(order: 1, quantity: 0m, amount: 0m),
            })));

        exception.Code.ShouldBe(RecipeLineQuantityGate.ZeroQuantityErrorCode);
        exception.Code.ShouldBe("TradeXpress:Product:Recipe:ZeroQuantity");
        exception.Data["LineOrder"].ShouldBe(2);   // kullanıcıya 1 tabanlı satır numarası
        exception.Data["Commodity"].ShouldBe(nameof(ProcessType.Metal));

        var persisted = await WithUnitOfWorkAsync(() => _recipeLines.GetListAsync(l => l.ProductVariantId == variantId));
        persisted.ShouldBeEmpty();
    }

    [Fact]
    public async Task Catalog_line_with_only_positive_quantity_is_accepted()
    {
        var companyId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        await WithUnitOfWorkAsync(() => _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
        {
            BuildMetalLine(order: 0, quantity: 3m, amount: 0m),
        }));

        var line = (await WithUnitOfWorkAsync(() => _recipeLines.GetListAsync(l => l.ProductVariantId == variantId)))
            .ShouldHaveSingleItem();
        line.Quantity.ShouldBe(3m);
        line.Amount.ShouldBe(0m);
    }

    [Fact]
    public async Task Catalog_line_with_only_positive_amount_is_accepted()
    {
        var companyId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        await WithUnitOfWorkAsync(() => _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
        {
            BuildMetalLine(order: 0, quantity: 0m, amount: 7.5m),
        }));

        var line = (await WithUnitOfWorkAsync(() => _recipeLines.GetListAsync(l => l.ProductVariantId == variantId)))
            .ShouldHaveSingleItem();
        line.Quantity.ShouldBe(0m);
        line.Amount.ShouldBe(7.5m);
    }

    [Fact]
    public async Task Service_line_with_zero_quantity_and_amount_is_out_of_scope_and_accepted()
    {
        var companyId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        await WithUnitOfWorkAsync(() => _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
        {
            new()
            {
                LineOrder = 0,
                ComponentType = RecipeComponentType.Service,
                DerivedBaseMode = RecipeDerivedBaseMode.AllAbove,
                DerivedOperation = RecipeDerivedOperation.Percent,
                DerivedOperand = 10m,
                Quantity = 0m,
                Amount = 0m,
            },
        }));

        var line = (await WithUnitOfWorkAsync(() => _recipeLines.GetListAsync(l => l.ProductVariantId == variantId)))
            .ShouldHaveSingleItem();
        line.ComponentType.ShouldBe(RecipeComponentType.Service);
    }

    /// <summary>Silinmek üzere gelen sıfır satırı guard'a TAKILMAZ — zaten gidiyor; kullanıcı bozuk satırı
    /// silerek kurtulabilmeli (aksi hâlde hatalı satır kaydı kilitlerdi).</summary>
    [Fact]
    public async Task Deleted_zero_line_does_not_block_the_save()
    {
        var companyId = Guid.NewGuid();
        var variantId = Guid.NewGuid();

        await WithUnitOfWorkAsync(() => _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
        {
            BuildMetalLine(order: 0, quantity: 2m, amount: 4m),
        }));
        var existing = (await WithUnitOfWorkAsync(() => _recipeLines.GetListAsync(l => l.ProductVariantId == variantId)))
            .ShouldHaveSingleItem();

        var deleted = BuildMetalLine(order: 0, quantity: 0m, amount: 0m);
        deleted.Id = existing.Id;
        deleted.IsDeleted = true;

        await WithUnitOfWorkAsync(() => _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
        {
            deleted,
            BuildMetalLine(order: 1, quantity: 1m, amount: 2m),
        }));

        var remaining = (await WithUnitOfWorkAsync(() => _recipeLines.GetListAsync(l => l.ProductVariantId == variantId)))
            .ShouldHaveSingleItem();
        remaining.Quantity.ShouldBe(1m);
    }

    private static ProductRecipeLineGraphDto BuildMetalLine(int order, decimal quantity, decimal amount)
    {
        return new ProductRecipeLineGraphDto
        {
            LineOrder = order,
            ComponentType = RecipeComponentType.CatalogCommodity,
            CommodityProcessType = ProcessType.Metal,
            CommodityId = Guid.NewGuid(),
            Quantity = quantity,
            Amount = amount,
            Factor = 0.916m,
            PaymentType = ProcessPaymentType.Normal,
        };
    }
}
