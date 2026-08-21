using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// KARAKTERİZASYON ağı (2026-07-16 kullanıcı kararı) — Product-özel varyant UZANTISI (SalePrice + reçete satırları;
/// <see cref="ProductVariantDetail"/> + <see cref="ProductVariantRecipeLine"/>) nitelik-değer EKLEMESİ tetiklediği
/// resync'te KOMBİNASYON DEĞİŞMEYEN varyantta korunur, YENİ kombinasyonda boş başlar. Public
/// <see cref="IProductAppService"/> üzerinden, client'ın <c>VariantGraphMerge</c>'ünün "kept" (var olan dto, aynı
/// Id) davranışını simüle ederek — gerçek risk zinciri (merge + extension upsert) uçtan uca kilitlenir. Gerçek
/// Sqlite repository'leriyle çalışır (EfCore concrete: EfCoreProductVariantExtensionSurvivalTests).
/// KIRMIZIYSA save/load zincirinde fiyat/reçete kaybı demektir — testi gevşetme, kök nedeni düzelt.
/// </summary>
public abstract class ProductVariantExtensionSurvivalTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IProductAppService _productAppService;
    private readonly ICurrentCompany _currentCompany;

    protected ProductVariantExtensionSurvivalTests()
    {
        _productAppService = GetRequiredService<IProductAppService>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task Unchanged_combination_keeps_SalePrice_and_recipe_lines_after_attribute_value_added()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            // 1) Ürün + tek nitelik (Renk: Kırmızı) → sunucu otomatik "Kırmızı" varyantını üretir (synchronizer).
            var created = await _productAppService.CreateAsync(new ProductCreateDto
            {
                Code = "TSTVAR1",
                Name = "Test Varyantlı Ürün",
                ProductCategoryId = await CreateTestProductCategoryAsync(),
                Attributes = new List<EntityAttributeGraphDto>
                {
                    BuildAttribute("Renk", "Kırmızı"),
                },
            });

            var kirmizi = created.Variants.ShouldHaveSingleItem();
            kirmizi.SalePrice.ShouldBeNull();
            kirmizi.RecipeLines.ShouldBeEmpty();

            // 2) Aynı kombinasyona fiyat + tek reçete satırı ver → uzantı ilk kez kaydolur.
            kirmizi.SalePrice = 100m;
            kirmizi.RecipeLines.Add(BuildManualRecipeLine(operand: 10m));

            var afterPrice = await _productAppService.UpdateAsync(created.Id, ToUpdateDto(created, kirmizi));
            var kirmiziWithPrice = afterPrice.Variants.ShouldHaveSingleItem();
            kirmiziWithPrice.Id.ShouldBe(kirmizi.Id);   // varyant Id resync boyunca DEĞİŞMEZ (aynı kombinasyon)
            kirmiziWithPrice.SalePrice.ShouldBe(100m);
            var savedLine = kirmiziWithPrice.RecipeLines.ShouldHaveSingleItem();
            savedLine.Id.ShouldNotBe(Guid.Empty);

            // 3) Niteliğe YENİ değer ekle (Mavi) — resync tetikler. Client VariantGraphMerge'ün "kept" davranışını
            //    simüle et: mevcut Kırmızı dto'sunu (AYNI Id, AYNI SalePrice/RecipeLines) OLDUĞU GİBİ yeniden gönder —
            //    yeni Mavi kombinasyonu için hiçbir varyant dto'su GÖNDERME (sunucu synchronizer'ı zaten üretir).
            //    KRİTİK: var olan "Renk" niteliğinin/"Kırmızı" değerinin GERÇEK DB Id'si korunur (afterPrice.Attributes'tan) —
            //    sıfırdan yeni Id'siz nitelik göndermek sunucuya YENİ nitelik açtırır (client hep var olanı yükleyip mutasyonlar).
            var renkAttribute = afterPrice.Attributes.Single(a => a.Name == "Renk");
            renkAttribute.Values.Add(new EntityAttributeValueGraphDto { Value = "Mavi" });
            var attributesWithTwoValues = afterPrice.Attributes;
            var afterResync = await _productAppService.UpdateAsync(created.Id, new ProductUpdateDto
            {
                Code = created.Code,
                Name = created.Name,
                IsActive = created.IsActive,
                ProductCategoryId = created.ProductCategoryId,
                Attributes = attributesWithTwoValues,
                Variants = new List<ProductVariantGraphDto> { kirmiziWithPrice },
            });

            // 4) Doğrula: Kırmızı (kombinasyon DEĞİŞMEDİ) → fiyat + reçete satırı (AYNI Id) korunur.
            //    Mavi (YENİ kombinasyon) → fiyatsız + reçetesiz başlar.
            afterResync.Variants.Count.ShouldBe(2);
            var kirmiziFinal = afterResync.Variants.Single(v => v.Id == kirmizi.Id);
            kirmiziFinal.SalePrice.ShouldBe(100m);
            var finalLine = kirmiziFinal.RecipeLines.ShouldHaveSingleItem();
            finalLine.Id.ShouldBe(savedLine.Id);   // AYNI reçete satırı — yeniden oluşturulmadı

            var maviFinal = afterResync.Variants.Single(v => v.Id != kirmizi.Id);
            maviFinal.SalePrice.ShouldBeNull();
            maviFinal.RecipeLines.ShouldBeEmpty();
        }
    }

    private static EntityAttributeGraphDto BuildAttribute(string name, params string[] values)
    {
        return new EntityAttributeGraphDto
        {
            Name = name,
            Values = values.Select(v => new EntityAttributeValueGraphDto { Value = v }).ToList(),
        };
    }

    private static ProductRecipeLineGraphDto BuildManualRecipeLine(decimal operand)
    {
        // Service + AllAbove: katalog emtia bağımlılığı yok (Metal/Jewelry/Stone seed'i gerekmez) — en yalın satır.
        return new ProductRecipeLineGraphDto
        {
            LineOrder = 0,
            ComponentType = RecipeComponentType.Service,
            DerivedBaseMode = RecipeDerivedBaseMode.AllAbove,
            DerivedOperation = RecipeDerivedOperation.Percent,
            DerivedOperand = operand,
        };
    }

    private static ProductUpdateDto ToUpdateDto(ProductGetDto p, ProductVariantGraphDto variant)
    {
        return new ProductUpdateDto
        {
            Code = p.Code,
            Name = p.Name,
            IsActive = p.IsActive,
            ProductCategoryId = p.ProductCategoryId,
            Attributes = p.Attributes,
            Variants = new List<ProductVariantGraphDto> { variant },
        };
    }
}
