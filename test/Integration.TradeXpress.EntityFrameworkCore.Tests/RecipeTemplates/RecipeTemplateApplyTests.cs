using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.RecipeTemplates;

/// <summary>
/// Reçete şablonunun ürüne uygulanmasının konvansiyon testiı — Hakan'ın "şablon devraldığı emtiaların ÜZERİNE
/// işleyecek" dediği davranışın sözleşmesi.
///
/// <para>Sınanan değişmezler: (1) emtia/kullanıcı satırlarına dokunulmaz, (2) şablon satırları EN SONA gelir
/// (hizmetler "üstümdeki her şey" üzerinden hesaplar), (3) yeniden uygulama satırları KATLAMAZ, (4) başka
/// şirketin şablonu uygulanamaz, (5) soy kimliği eşlemesi (2026-08-21 Hakan onaylı çoğalma düzeltmesi):
/// düzenlenmiş satır varken yeniden uygulama satır sayısını SABİT tutar, düzenlemeyi EZMEZ, dokunulmamış
/// satırın kimliğini korur — kimliksiz (özellik öncesi) eski satırda ise eski davranış kırılmaz.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class RecipeTemplateApplyTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string ProductVariantEntityName = "Product";

    private readonly IRecipeTemplateAppService _templateAppService;
    private readonly IProductAppService _productAppService;
    private readonly IProductCategoryAppService _categoryAppService;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLines;
    private readonly IRepository<EntityVariant, Guid> _variants;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ProductRecipeLineWriter _writer;

    public RecipeTemplateApplyTests()
    {
        _templateAppService = GetRequiredService<IRecipeTemplateAppService>();
        _productAppService = GetRequiredService<IProductAppService>();
        _categoryAppService = GetRequiredService<IProductCategoryAppService>();
        _recipeLines = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
        _variants = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
        _writer = GetRequiredService<ProductRecipeLineWriter>();
    }

    [Fact]
    public async Task Template_lines_are_appended_after_the_existing_recipe()
    {
        await InCompanyAsync(async () =>
        {
            var product = await CreateProductAsync("URN-A", "Şablon Ürünü");
            var variantId = await GetSingleVariantIdAsync(product.Id);

            // Ürünün mevcut (kullanıcı) satırı — şablon bunun ÜSTÜNE işlemeli, ezmemeli.
            await InsertManualLineAsync(product.Id, variantId, lineOrder: 0);

            var template = await CreateTemplateAsync("Standart Paketleme", operand: 12m);
            await _templateAppService.ApplyToProductAsync(template.Id, product.Id);

            var lines = await LoadLinesAsync(variantId);

            lines.Count.ShouldBe(2);
            lines[0].Origin.ShouldBe(RecipeLineOrigin.Manual);      // kullanıcı satırı korundu
            lines[1].Origin.ShouldBe(RecipeLineOrigin.Template);    // şablon EN SONA
            lines[1].DerivedOperand.ShouldBe(12m);
        });
    }

    [Fact]
    public async Task Re_applying_a_template_does_not_duplicate_its_lines()
    {
        // İdempotanlık: kullanıcı "uygula"ya iki kez basarsa maliyet iki katına çıkmamalı.
        await InCompanyAsync(async () =>
        {
            var product = await CreateProductAsync("URN-B", "Tekrar Uygulanan");
            var variantId = await GetSingleVariantIdAsync(product.Id);
            var template = await CreateTemplateAsync("Kargo", operand: 5m);

            await _templateAppService.ApplyToProductAsync(template.Id, product.Id);
            await _templateAppService.ApplyToProductAsync(template.Id, product.Id);

            var lines = await LoadLinesAsync(variantId);

            lines.Count(l => l.Origin == RecipeLineOrigin.Template).ShouldBe(1);
        });
    }

    [Fact]
    public async Task Re_applying_after_an_edit_keeps_the_line_count_fixed()
    {
        // Çoğalma düzeltmesi (2026-08-21 Hakan onayı): kullanıcı şablondan gelen bir satırı düzenledikten sonra
        // yeniden uygulama, düzenlenen satırın şablon karşılığını SourceTemplateLineId ile tanır ve YENİDEN
        // KURMAZ — satır sayısı sabit kalır, düzenleme ezilmez, kalem iki kez görünmez.
        await InCompanyAsync(async () =>
        {
            var product = await CreateProductAsync("URN-F", "Sabit Sayı");
            var variantId = await GetSingleVariantIdAsync(product.Id);

            var template = await _templateAppService.CreateAsync(new RecipeTemplateCreateDto
            {
                Name = "İki Kalem",
                Lines = new List<RecipeTemplateLineDto>
                {
                    NewServiceLine(order: 0, operand: 10m),
                    NewServiceLine(order: 1, operand: 20m),
                },
            });

            await _templateAppService.ApplyToProductAsync(template.Id, product.Id);

            var applied = await LoadLinesAsync(variantId);
            applied.Count.ShouldBe(2);
            applied.ShouldAllBe(
                l => l.SourceTemplateLineId != null,
                "Şablondan inen satır soy kimliğini taşımalı — kimlik olmadan yeniden uygulama eşleyemez.");

            // Kullanıcı BİRİNCİ satırın oranını düzeltir (10 → 99) — üretimdeki tek yol: yazıcı (sahiplenme orada).
            var companyId = applied[0].CompanyId;
            var untouchedLineId = applied[1].Id;
            await WithUnitOfWorkAsync(async () =>
            {
                await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
                {
                    NewServiceGraphLine(applied[0].Id, lineOrder: 0, operand: 99m),
                    NewServiceGraphLine(applied[1].Id, lineOrder: 1, operand: 20m),
                });
                return true;
            });

            var result = await _templateAppService.ApplyToProductAsync(template.Id, product.Id);

            result.PreservedEditedLineCount.ShouldBe(
                0,
                "Kimliği eşleşen düzenlenmiş satırın şablon karşılığı kurulmadığına göre kalem TEK görünür — " +
                "sayıya girseydi UI yanlış 'iki kez görünüyor' uyarısı verirdi.");

            var after = await LoadLinesAsync(variantId);
            after.Count.ShouldBe(2, "Yeniden uygulama satır sayısını değiştirmemeli — çoğalma tam buydu.");

            var owned = after.Single(l => l.Origin == RecipeLineOrigin.TemplateEdited);
            owned.DerivedOperand.ShouldBe(99m, "Kullanıcının düzenlemesi EZİLMEMELİ (sahiplik kuralı).");

            var refreshed = after.Single(l => l.Origin == RecipeLineOrigin.Template);
            refreshed.DerivedOperand.ShouldBe(20m);
            refreshed.Id.ShouldBe(
                untouchedLineId,
                "Dokunulmamış şablon satırı YERİNDE tazelenmeli — satır kimliği değişirse kullanıcının " +
                "SelectedLines türev referansları kopar.");
        });
    }

    [Fact]
    public async Task An_untouched_line_keeps_its_identity_and_takes_the_templates_new_values()
    {
        // Kimlik düzeltmesinin öbür yüzü: dokunulmamış satır artık düşür+yeniden-kur değil, yerinde tazele —
        // şablon değişikliği değeri taşır ama satır Id'si sabit kalır.
        await InCompanyAsync(async () =>
        {
            var product = await CreateProductAsync("URN-G", "Kimlik Korunur");
            var variantId = await GetSingleVariantIdAsync(product.Id);

            var template = await CreateTemplateAsync("Tazelenen", operand: 5m);
            var templateLineId = template.Lines.Single().Id;

            await _templateAppService.ApplyToProductAsync(template.Id, product.Id);

            var appliedLine = (await LoadLinesAsync(variantId)).ShouldHaveSingleItem();
            appliedLine.SourceTemplateLineId.ShouldBe(templateLineId);

            // Şablonun kendisi güncellenir (5 → 9; satır kimliği merger'da korunur) ve yeniden uygulanır.
            await _templateAppService.UpdateAsync(template.Id, new RecipeTemplateUpdateDto
            {
                Name = template.Name,
                IsActive = true,
                Lines = new List<RecipeTemplateLineDto>
                {
                    new()
                    {
                        Id = templateLineId,
                        LineOrder = 0,
                        ComponentType = RecipeComponentType.Service,
                        DerivedOperation = RecipeDerivedOperation.Percent,
                        DerivedOperand = 9m,
                    },
                },
            });
            await _templateAppService.ApplyToProductAsync(template.Id, product.Id);

            var refreshed = (await LoadLinesAsync(variantId)).ShouldHaveSingleItem();
            refreshed.Id.ShouldBe(appliedLine.Id, "Dokunulmamış satır yerinde tazelenmeli — kimlik sabit.");
            refreshed.DerivedOperand.ShouldBe(9m, "Şablonun yeni değeri satıra inmeli.");
            refreshed.Origin.ShouldBe(RecipeLineOrigin.Template);
        });
    }

    [Fact]
    public async Task A_pre_feature_line_without_identity_keeps_the_old_behavior()
    {
        // Özellik öncesi satırda SourceTemplateLineId null'dır — eşlenemez. Eski davranış kırılmamalı:
        // dokunulmamış Template satırı düşürülüp yeniden kurulur; düzenlenmiş (TemplateEdited) satır korunur,
        // şablon karşılığı tanınamadığı için yeniden kurulur ve sayı uyarı olarak çağırana döner.
        await InCompanyAsync(async () =>
        {
            var product = await CreateProductAsync("URN-H", "Eski Satırlı");
            var variantId = await GetSingleVariantIdAsync(product.Id);
            var companyId = (await GetRequiredService<IRepository<Product, Guid>>().GetAsync(product.Id)).CompanyId;

            await InsertLegacyServiceLineAsync(companyId, variantId, RecipeLineOrigin.Template, operand: 7m, lineOrder: 0);
            await InsertLegacyServiceLineAsync(companyId, variantId, RecipeLineOrigin.TemplateEdited, operand: 42m, lineOrder: 1);

            var template = await CreateTemplateAsync("Yeni Şablon", operand: 5m);
            var result = await _templateAppService.ApplyToProductAsync(template.Id, product.Id);

            result.PreservedEditedLineCount.ShouldBe(
                1, "Kimliksiz düzenlenmiş satırın kalemi hâlâ iki kez görünebilir — uyarı sürmeli (eski davranış).");

            var after = await LoadLinesAsync(variantId);
            after.Count.ShouldBe(2);

            var preservedLine = after.Single(l => l.Origin == RecipeLineOrigin.TemplateEdited);
            preservedLine.DerivedOperand.ShouldBe(42m, "Düzenlenmiş eski satır korunmalı.");
            preservedLine.SourceTemplateLineId.ShouldBeNull();

            // Kimliksiz dokunulmamış satır (7) düştü; yerine şablonun satırı (5) SOY KİMLİĞİYLE kuruldu —
            // bundan sonraki uygulamalar artık eşleyebilir (ileriye dönük iyileşme).
            var rebuilt = after.Single(l => l.Origin == RecipeLineOrigin.Template);
            rebuilt.DerivedOperand.ShouldBe(5m);
            rebuilt.SourceTemplateLineId.ShouldBe(template.Lines.Single().Id);
        });
    }

    [Fact]
    public async Task Applying_a_second_template_replaces_the_first_ones_lines()
    {
        // Şablon satırları TEK kaynaktan yönetilir: ikinci şablon öncekinin satırlarını devralmaz, değiştirir.
        // (Kullanıcı iki şablonu birleştirmek isterse satırları elle ekler — belirsiz birikme olmaz.)
        await InCompanyAsync(async () =>
        {
            var product = await CreateProductAsync("URN-C", "İki Şablon");
            var variantId = await GetSingleVariantIdAsync(product.Id);

            var first = await CreateTemplateAsync("Birinci", operand: 3m);
            var second = await CreateTemplateAsync("İkinci", operand: 7m);

            await _templateAppService.ApplyToProductAsync(first.Id, product.Id);
            await _templateAppService.ApplyToProductAsync(second.Id, product.Id);

            var templateLines = (await LoadLinesAsync(variantId))
                .Where(l => l.Origin == RecipeLineOrigin.Template)
                .ToList();

            templateLines.Count.ShouldBe(1);
            templateLines[0].DerivedOperand.ShouldBe(7m);
        });
    }

    [Fact]
    public async Task Template_lines_keep_their_relative_order()
    {
        await InCompanyAsync(async () =>
        {
            var product = await CreateProductAsync("URN-D", "Sıralı Şablon");
            var variantId = await GetSingleVariantIdAsync(product.Id);

            var template = await _templateAppService.CreateAsync(new RecipeTemplateCreateDto
            {
                Name = "Çok Satırlı",
                Lines = new List<RecipeTemplateLineDto>
                {
                    NewServiceLine(order: 0, operand: 10m, SideCostKind.Packaging),
                    NewServiceLine(order: 1, operand: 20m, SideCostKind.Cargo),
                    NewServiceLine(order: 2, operand: 30m, SideCostKind.InsuredShipping),
                },
            });

            await _templateAppService.ApplyToProductAsync(template.Id, product.Id);

            var applied = (await LoadLinesAsync(variantId))
                .Where(l => l.Origin == RecipeLineOrigin.Template)
                .OrderBy(l => l.LineOrder)
                .ToList();

            applied.Select(l => l.DerivedOperand).ShouldBe(new[] { 10m, 20m, 30m });
            applied.Select(l => l.SideCostKind).ShouldBe(
                new SideCostKind?[] { SideCostKind.Packaging, SideCostKind.Cargo, SideCostKind.InsuredShipping });
        });
    }

    [Fact]
    public async Task A_template_from_another_company_cannot_be_applied()
    {
        var foreignTemplateId = Guid.Empty;

        await InCompanyAsync(async () =>
        {
            foreignTemplateId = (await CreateTemplateAsync("Yabancı Şablon", operand: 1m)).Id;
        });

        await InCompanyAsync(async () =>
        {
            var product = await CreateProductAsync("URN-E", "Kaçak Uygulama");

            await Should.ThrowAsync<AbpException>(
                () => _templateAppService.ApplyToProductAsync(foreignTemplateId, product.Id));
        });
    }

    [Fact]
    public async Task Template_name_is_unique_per_company()
    {
        await InCompanyAsync(async () =>
        {
            await CreateTemplateAsync("Aynı Ad", operand: 1m);

            var error = await Should.ThrowAsync<BusinessException>(() => CreateTemplateAsync("Aynı Ad", operand: 2m));
            error.Code.ShouldBe("TradeXpress:RecipeTemplate:NameAlreadyExists");
        });
    }

    [Fact]
    public async Task Template_lines_keep_their_ids_across_updates()
    {
        // Kategori nitelikleriyle aynı gerekçe: satır kimliği korunmazsa düzenleme geçmişi ve ileride
        // kurulacak referanslar her kaydetmede kopar.
        await InCompanyAsync(async () =>
        {
            var template = await CreateTemplateAsync("Kimlik Testi", operand: 4m);
            var lineId = template.Lines.Single().Id;

            lineId.ShouldNotBe(Guid.Empty);

            var updated = await _templateAppService.UpdateAsync(template.Id, new RecipeTemplateUpdateDto
            {
                Name = template.Name,
                IsActive = true,
                Lines = new List<RecipeTemplateLineDto>
                {
                    new()
                    {
                        Id = lineId,
                        LineOrder = 0,
                        ComponentType = RecipeComponentType.Service,
                        DerivedOperation = RecipeDerivedOperation.Percent,
                        DerivedOperand = 9m,
                    },
                },
            });

            var line = updated.Lines.ShouldHaveSingleItem();
            line.Id.ShouldBe(lineId);
            line.DerivedOperand.ShouldBe(9m);
        });
    }

    private static RecipeTemplateLineDto NewServiceLine(int order, decimal operand, SideCostKind? kind = null)
    {
        return new RecipeTemplateLineDto
        {
            LineOrder = order,
            ComponentType = RecipeComponentType.Service,
            DerivedOperation = RecipeDerivedOperation.Percent,
            DerivedOperand = operand,
            SideCostKind = kind,
        };
    }

    private Task<RecipeTemplateGetDto> CreateTemplateAsync(string name, decimal operand)
    {
        return _templateAppService.CreateAsync(new RecipeTemplateCreateDto
        {
            Name = name,
            Lines = new List<RecipeTemplateLineDto> { NewServiceLine(order: 0, operand) },
        });
    }

    /// <summary>Ürün kategorisi ZORUNLU (kanal kategorisi + komisyon oradan çözülür) → her ürün için önce bir
    /// kategori açılır. Testin konusu kategori değil; kurulum burada tek yerde durur.</summary>
    private async Task<ProductGetDto> CreateProductAsync(string code, string name)
    {
        var category = await _categoryAppService.CreateAsync(new ProductCategoryCreateDto { Name = code + " Kategorisi" });
        return await _productAppService.CreateAsync(
            new ProductCreateDto { Code = code, Name = name, ProductCategoryId = category.Id });
    }

    /// <summary>Ürünün tek (ana) varyantının kimliği — agnostik varyant sistemi ürün kaydında bir varyant kurar.</summary>
    private async Task<Guid> GetSingleVariantIdAsync(Guid productId)
    {
        var variants = await _variants.GetListAsync(
            v => v.EntityName == ProductVariantEntityName && v.EntityId == productId);
        return variants.Single().Id;
    }

    private async Task InsertManualLineAsync(Guid productId, Guid variantId, int lineOrder)
    {
        var product = await GetRequiredService<IRepository<Product, Guid>>().GetAsync(productId);
        var line = new ProductVariantRecipeLine(
            product.CompanyId, variantId, RecipeComponentType.Service, lineOrder);
        line.SetService(null, RecipeDerivedBaseMode.AllAbove, RecipeDerivedOperation.Percent, 2m, null);
        await _recipeLines.InsertAsync(line, autoSave: true);
    }

    /// <summary>ÖZELLİK ÖNCESİ şablon satırını taklit eder: origin şablon-soylu ama SourceTemplateLineId NULL
    /// (kimlik alanı yokken yazılmış satır) — eski-davranış testlerinin kurulumu.</summary>
    private async Task InsertLegacyServiceLineAsync(
        Guid companyId, Guid variantId, RecipeLineOrigin origin, decimal operand, int lineOrder)
    {
        var line = new ProductVariantRecipeLine(companyId, variantId, RecipeComponentType.Service, lineOrder);
        line.SetService(null, RecipeDerivedBaseMode.AllAbove, RecipeDerivedOperation.Percent, operand, null);
        line.SetOrigin(origin);
        await _recipeLines.InsertAsync(line, autoSave: true);
    }

    /// <summary>Yazıcıya giden hizmet satırı graf düğümü — uygulayıcının kurduğu satırın birebir karşılığı
    /// (taban AllAbove; şablon satırı seçili-satır referansı taşıyamaz).</summary>
    private static ProductRecipeLineGraphDto NewServiceGraphLine(Guid id, int lineOrder, decimal operand)
    {
        return new ProductRecipeLineGraphDto
        {
            Id = id,
            LineOrder = lineOrder,
            ComponentType = RecipeComponentType.Service,
            DerivedBaseMode = RecipeDerivedBaseMode.AllAbove,
            DerivedOperation = RecipeDerivedOperation.Percent,
            DerivedOperand = operand,
        };
    }

    private async Task<List<ProductVariantRecipeLine>> LoadLinesAsync(Guid variantId)
    {
        var lines = await _recipeLines.GetListAsync(l => l.ProductVariantId == variantId);
        return lines.OrderBy(l => l.LineOrder).ToList();
    }

    /// <summary>Her test kendi tenant+şirketinde çalışır (şablon adı benzersizliği şirket kapsamındadır).</summary>
    private async Task InCompanyAsync(Func<Task> body)
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var companyId = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = companyId;
            try
            {
                await body();
            }
            finally
            {
                _companyContext.CompanyId = null;
            }
        }
    }
}
