using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürün varyant modu SUNUCU KAPISI testleri (Dilim-3) — public <see cref="IProductAppService"/> yüzeyinden:
/// <list type="bullet">
///   <item>(a) KORUMA: VariantMode göndermeyen mevcut akış MultiVariant statükosunda kalır.</item>
///   <item>(b) SingleVariant kapısı: DÜŞMANCA nitelikli graf gönderilse bile sunucu nitelik grafını boşaltır →
///   synchronizer tek ana varyanta indirir (client güven sınırı DEĞİLDİR).</item>
///   <item>(c) Muadil konfigürasyon fail-fast'leri servis yolundan da çalışır (entity mutator'ı Create/Update'te).</item>
///   <item>(e) Muadil modunda uygulanan kombinasyon reçete satırları <c>CommodityVariantId</c>'li persist olur
///   (SaveRecipeLinesAsync yolu) + konfigürasyon alanları round-trip eder.</item>
/// </list>
/// KIRMIZIYSA mod kapısı deliktir (nitelik grafı sızar) ya da muadil konfigürasyonu kaybolur — testi gevşetme.
/// </summary>
public abstract class ProductVariantModeGateTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IProductAppService _productAppService;
    private readonly ISalesChannelTrN11ProductAppService _n11ProductAppService;
    private readonly IRepository<SalesChannelTrN11, Guid> _n11ChannelRepository;
    private readonly ICurrentCompany _currentCompany;

    protected ProductVariantModeGateTests()
    {
        _productAppService = GetRequiredService<IProductAppService>();
        _n11ProductAppService = GetRequiredService<ISalesChannelTrN11ProductAppService>();
        _n11ChannelRepository = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task Create_without_variant_mode_stays_MultiVariant_status_quo()
    {
        using (_currentCompany.Change(Guid.NewGuid()))
        {
            var created = await _productAppService.CreateAsync(new ProductCreateDto
            {
                Code = "TSTMODEA",
                Name = "Statüko Ürünü",
            });

            created.VariantMode.ShouldBe(ProductVariantMode.MultiVariant);
            created.SubstitutionGroupId.ShouldBeNull();
            created.SubstitutionOverrideVariantIds.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task SingleVariant_gate_collapses_hostile_attribute_graph_to_single_main_variant()
    {
        using (_currentCompany.Change(Guid.NewGuid()))
        {
            // 1) MultiVariant ürün + nitelik (Renk: Kırmızı/Mavi) → synchronizer 2 varyant üretir.
            var created = await _productAppService.CreateAsync(new ProductCreateDto
            {
                Code = "TSTMODEB",
                Name = "Tek Varyant Kapı Ürünü",
                Attributes = new List<EntityAttributeGraphDto> { BuildAttribute("Renk", "Kırmızı", "Mavi") },
            });
            created.Variants.Count.ShouldBe(2);

            // 2) DÜŞMANCA update: mod SingleVariant AMA nitelik grafı + çoklu varyantlar hâlâ gönderiliyor
            //    (client kapısı atlatılmış gibi) → sunucu kapısı nitelikleri boşaltmalı.
            var after = await _productAppService.UpdateAsync(created.Id, new ProductUpdateDto
            {
                Code = created.Code,
                Name = created.Name,
                IsActive = created.IsActive,
                VariantMode = ProductVariantMode.SingleVariant,
                Attributes = created.Attributes,
                Variants = created.Variants,
            });

            after.VariantMode.ShouldBe(ProductVariantMode.SingleVariant);
            after.Attributes.ShouldBeEmpty();   // nitelik grafı sunucuda boşaltıldı
            var main = after.Variants.ShouldHaveSingleItem();
            main.IsMain.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Substitution_mode_without_group_fails_fast_through_service()
    {
        using (_currentCompany.Change(Guid.NewGuid()))
        {
            var exception = await Should.ThrowAsync<BusinessException>(() => _productAppService.CreateAsync(
                new ProductCreateDto
                {
                    Code = "TSTMODEC",
                    Name = "Grupsuz Muadil Ürünü",
                    VariantMode = ProductVariantMode.Substitution,
                    SubstitutionTargetQuantity = 10m,
                }));
            exception.Code.ShouldBe("TradeXpress:Product:SubstitutionGroupRequired");
        }
    }

    [Fact]
    public async Task Substitution_config_and_variant_recipe_lines_round_trip_with_variant_ids()
    {
        using (_currentCompany.Change(Guid.NewGuid()))
        {
            var groupId = Guid.NewGuid();
            var overrideVariantId = Guid.NewGuid();
            var metalId = Guid.NewGuid();
            var metalVariantId = Guid.NewGuid();

            // Muadil ürün: tek ana varyant + "Reçeteye Uygula" çıktısını temsil eden CommodityVariantId'li metal satırı
            // (satır kurulumu host BuildTrialRecipeLine / köprü BuildRecipeLineDtos alan kümesi).
            var created = await _productAppService.CreateAsync(new ProductCreateDto
            {
                Code = "TSTMODED",
                Name = "Muadil Paket Ürünü",
                VariantMode = ProductVariantMode.Substitution,
                SubstitutionGroupId = groupId,
                SubstitutionTargetQuantity = 10m,
                SubstitutionToleranceType = ToleranceType.Amount,
                SubstitutionToleranceValue = 0.5m,
                SubstitutionOverrideVariantIds = new List<Guid> { overrideVariantId },
                Variants = new List<ProductVariantGraphDto>
                {
                    new()
                    {
                        IsMain = true,
                        IsActive = true,
                        Code = ProductConsts.MainVariantCode,
                        Name = ProductConsts.MainVariantName,
                        RecipeLines = new List<ProductRecipeLineGraphDto>
                        {
                            new()
                            {
                                LineOrder = 0,
                                ComponentType = RecipeComponentType.CatalogCommodity,
                                CommodityProcessType = ProcessType.Metal,
                                CommodityId = metalId,
                                CommodityVariantId = metalVariantId,
                                Quantity = 2m,
                                Amount = 10m,
                                Factor = 1m,
                                PaymentType = ProcessPaymentType.Normal,
                                PayFactor = 3m,
                            },
                        },
                    },
                },
            });

            // Muadil modu = tek ana varyant (nitelik-tabanlı üretim BYPASS).
            var mainVariant = created.Variants.ShouldHaveSingleItem();
            mainVariant.IsMain.ShouldBeTrue();

            // Yeniden yükle: konfigürasyon + reçete satırı (CommodityVariantId dahil) persist edilmiş olmalı.
            var reloaded = await _productAppService.GetAsync(created.Id);
            reloaded.VariantMode.ShouldBe(ProductVariantMode.Substitution);
            reloaded.SubstitutionGroupId.ShouldBe(groupId);
            reloaded.SubstitutionTargetQuantity.ShouldBe(10m);
            reloaded.SubstitutionToleranceType.ShouldBe(ToleranceType.Amount);
            reloaded.SubstitutionToleranceValue.ShouldBe(0.5m);
            reloaded.SubstitutionOverrideVariantIds.ShouldBe(new List<Guid> { overrideVariantId });

            var line = reloaded.Variants.ShouldHaveSingleItem().RecipeLines.ShouldHaveSingleItem();
            line.Id.ShouldNotBe(Guid.Empty);
            line.CommodityId.ShouldBe(metalId);
            line.CommodityVariantId.ShouldBe(metalVariantId);
            line.Quantity.ShouldBe(2m);

            // Mod MultiVariant'a dönerse muadil konfigürasyonu TEMİZLENİR (bayat grup/hedef taşınmaz).
            var backToMulti = await _productAppService.UpdateAsync(created.Id, new ProductUpdateDto
            {
                Code = reloaded.Code,
                Name = reloaded.Name,
                IsActive = reloaded.IsActive,
                VariantMode = ProductVariantMode.MultiVariant,
                Variants = reloaded.Variants,
            });
            backToMulti.SubstitutionGroupId.ShouldBeNull();
            backToMulti.SubstitutionTargetQuantity.ShouldBeNull();
            backToMulti.SubstitutionOverrideVariantIds.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Channel_clone_chain_carries_recipe_variant_ids_to_channel_stock_item()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var metalId = Guid.NewGuid();
            var metalVariantId = Guid.NewGuid();

            // Muadil ürünü: tek ana varyant + CommodityVariantId'li kombinasyon reçetesi ("Reçeteye Uygula" çıktısı).
            var product = await _productAppService.CreateAsync(new ProductCreateDto
            {
                Code = "TSTMODEE",
                Name = "Kanal Yansıma Ürünü",
                VariantMode = ProductVariantMode.Substitution,
                SubstitutionGroupId = Guid.NewGuid(),
                SubstitutionTargetQuantity = 10m,
                Variants = new List<ProductVariantGraphDto>
                {
                    new()
                    {
                        IsMain = true,
                        IsActive = true,
                        Code = ProductConsts.MainVariantCode,
                        Name = ProductConsts.MainVariantName,
                        RecipeLines = new List<ProductRecipeLineGraphDto>
                        {
                            new()
                            {
                                LineOrder = 0,
                                ComponentType = RecipeComponentType.CatalogCommodity,
                                CommodityProcessType = ProcessType.Metal,
                                CommodityId = metalId,
                                CommodityVariantId = metalVariantId,
                                Quantity = 2m,
                                Amount = 10m,
                                Factor = 1m,
                                PaymentType = ProcessPaymentType.Normal,
                                PayFactor = 3m,
                            },
                        },
                    },
                },
            });

            // Kanal-ürünü aç (özelliksiz → legacy graf) — klon zinciri (A6 sonrası varyant-koruyan) ERP reçetesini
            // kanal grafına CommodityVariantId'siyle taşımalı (ek iş YOK; bu test kanıt ağı).
            var channel = await WithUnitOfWorkAsync(() => _n11ChannelRepository.InsertAsync(
                new SalesChannelTrN11(companyId, "N11-TSTMODEE", "N11 Kanal TSTMODEE", "app-key", "app-secret"),
                autoSave: true));
            var channelProduct = await _n11ProductAppService.CreateAsync(new SalesChannelTrN11ProductCreateDto
            {
                ProductId = product.Id,
                SalesChannelId = channel.Id,
                CategoryExternalId = "1000846",
                ShipmentTemplateName = "Standart Teslimat",
            });

            var graph = await _n11ProductAppService.GetAsync(channelProduct.Id);
            var stockItem = graph.StockItems.ShouldHaveSingleItem();   // tek ana varyant (Muadil modu)
            var clonedLine = stockItem.RecipeLines.Where(l => l.SideCostKind == null).ShouldHaveSingleItem();
            clonedLine.CommodityId.ShouldBe(metalId);
            clonedLine.CommodityVariantId.ShouldBe(metalVariantId);   // varyant-koruyan klon (A6)
            clonedLine.Quantity.ShouldBe(2m);
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
}
