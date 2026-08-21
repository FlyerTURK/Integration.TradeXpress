using System;
using System.Collections.Generic;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.RecipeTemplates;

/// <summary>
/// Reçete ŞABLONU için "ana emtia 0 adet / 0 miktar girilmez" guard'ı (2026-08-19 Hakan kuralı) —
/// <see cref="RecipeTemplateLineMerger"/> saf testleri (DB'siz). Şablon satırı ürüne AYNEN kopyalandığından
/// (<c>RecipeTemplateApplier</c>) sıfır satırı şablon üzerinden reçeteye sızabilirdi; guard ürün reçetesi
/// yazıcısıyla AYNI kod ve AYNI veriyle reddeder ve şablona HİÇ dokunmaz (fail-fast).
/// </summary>
public class RecipeTemplateLineMergerZeroQuantityTests
{
    [Fact]
    public void Zero_quantity_and_amount_catalog_line_is_rejected_and_template_stays_untouched()
    {
        var template = new RecipeTemplate(Guid.NewGuid(), "Kapı Şablonu");

        var exception = Should.Throw<BusinessException>(() => RecipeTemplateLineMerger.Apply(template, new List<RecipeTemplateLineDto>
        {
            ServiceLine(order: 0),
            CatalogLine(order: 1, quantity: 0m, amount: 0m),
        }));

        exception.Code.ShouldBe(RecipeLineQuantityGate.ZeroQuantityErrorCode);
        exception.Data["LineOrder"].ShouldBe(2);
        template.Lines.ShouldBeEmpty();   // hizmet satırı bile yazılmadı — kısmi birleştirme yok
    }

    [Fact]
    public void Catalog_line_with_positive_quantity_or_amount_merges()
    {
        var template = new RecipeTemplate(Guid.NewGuid(), "Kapı Şablonu");

        RecipeTemplateLineMerger.Apply(template, new List<RecipeTemplateLineDto>
        {
            CatalogLine(order: 0, quantity: 1m, amount: 0m),
            CatalogLine(order: 1, quantity: 0m, amount: 4m),
            ServiceLine(order: 2),
        });

        template.Lines.Count.ShouldBe(3);
    }

    private static RecipeTemplateLineDto CatalogLine(int order, decimal quantity, decimal amount)
    {
        return new RecipeTemplateLineDto
        {
            LineOrder = order,
            ComponentType = RecipeComponentType.CatalogCommodity,
            CommodityProcessType = ProcessType.Metal,
            CommodityId = Guid.NewGuid(),
            Quantity = quantity,
            Amount = amount,
            Factor = 0.916m,
        };
    }

    private static RecipeTemplateLineDto ServiceLine(int order)
    {
        return new RecipeTemplateLineDto
        {
            LineOrder = order,
            ComponentType = RecipeComponentType.Service,
            DerivedOperation = RecipeDerivedOperation.Percent,
            DerivedOperand = 5m,
        };
    }
}
