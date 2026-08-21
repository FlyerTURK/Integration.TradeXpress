using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.RecipeTemplates;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// KULLANICI DÜZENLERSE SATIR ONUNDUR — şablondan gelen reçete satırının sahiplenme ağı
/// (2026-08-20 Hakan kuralı: <i>"Uygula buttonu olsun ki … mevcuttaki kayıtlar değer değişince kolayca
/// silinmesin. Yine şablon varyantlarda kullanıcı değişikliğine müsait olsun."</i>).
///
/// <para><b>Sabitlenen delik:</b> <see cref="RecipeTemplateApplier"/> yeniden uygulamada
/// <see cref="RecipeLineOrigin.Template"/> satırlarını SİLİP yeniden kuruyordu. Kullanıcı şablondan gelen bir
/// satırın miktarını/işçiliğini varyantta düzelttiyse ikinci "Uygula" o emeği SESSİZCE yok ediyordu — üstelik
/// <see cref="RecipeLineOrigin"/> dokümanı "düzenlemesi korunur" DİYORDU. Kural artık kayıt yolunda yaşıyor:
/// düzenlenen satır <see cref="RecipeLineOrigin.TemplateEdited"/>'e geçer ve tazeleme sorgusuna hiç girmez.</para>
///
/// <para><b>Sahiplenme neden <see cref="RecipeLineOrigin.Manual"/>'a YAZILMAZ:</b> satırın şablon SOYU üç ayrı
/// yolun okuduğu bilgidir (muadil denemesini reçeteye uygulama · muadil önizlemesi · materyalizasyonun "bu
/// varyantta zaten şablon satırı var mı" nöbetçisi). Soy silinseydi korunan satır sırasıyla kalıcı SİLİNİR,
/// ekrandan düşer ve üstüne ikinci bir şablon seti serilirdi — yani kuralın kendisi koruduğu şeyi öldürürdü.</para>
///
/// <para><b>Sınanan değişmezler:</b> (1) düzenleme sahiplendirir ve SOYU KORUR, (2) dokunulmamış satır şablonun
/// malı kalır, (3) yeniden uygulamada düzenlenmiş satır KORUNUR / dokunulmamış satır TAZELENİR ve korunan satır
/// sayısı çağırana BİLDİRİLİR (kullanıcı iki kez görünen kalemi uyarısız bulmasın), (4) yalnız sıra değişimi
/// sahiplendirmez (sıra her kaydetmede yeniden numaralanır — kullanıcı niyeti değildir).</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class RecipeLineOwnershipTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ProductRecipeLineWriter _writer;
    private readonly RecipeTemplateApplier _applier;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLines;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ICurrentTenant _currentTenant;

    public RecipeLineOwnershipTests()
    {
        _writer = GetRequiredService<ProductRecipeLineWriter>();
        _applier = GetRequiredService<RecipeTemplateApplier>();
        _recipeLines = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Editing_a_template_line_hands_it_over_to_the_user()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;
        var variantId = Guid.NewGuid();
        var metalId = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            var lineId = await InsertTemplateMetalLineAsync(companyId, variantId, metalId, quantity: 2m, amount: 4m);

            // Kullanıcı varyantta miktarı düzeltir (2 → 3) — üretimdeki tek yol: yazıcı.
            // TEK UoW: yerinde güncelleme sonrası kıyasın hâlâ çalıştığını da zorlar (EF kimlik haritası tuzağı).
            await WithUnitOfWorkAsync(async () =>
            {
                await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
                {
                    BuildMetalLine(metalId, quantity: 3m, amount: 6m, id: lineId),
                });
                return true;
            });

            var line = await _recipeLines.GetAsync(lineId);
            line.Quantity.ShouldBe(3m);
            line.Origin.ShouldBe(
                RecipeLineOrigin.TemplateEdited,
                "Şablondan gelen satır düzenlendiğinde kullanıcıya devredilmeli — aksi halde yeniden uygulama onu " +
                "siler. Manual DEĞİL: şablon soyu korunmazsa satırı 'şablon satırı olduğu için' koruyan yollar " +
                "onu tanıyamaz.");
        }
    }

    [Fact]
    public async Task An_untouched_template_line_stays_the_templates_own()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;
        var variantId = Guid.NewGuid();
        var metalId = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            var lineId = await InsertTemplateMetalLineAsync(companyId, variantId, metalId, quantity: 2m, amount: 4m);

            // Kullanıcı reçeteyi AÇIP kaydeder ama satıra dokunmaz (aynı değerler geri gelir) — sahiplenme YOK.
            // Aksi halde formu bir kez açıp kaydetmek bile şablonun tazeleme yetkisini kalıcı olarak kaldırırdı.
            await WithUnitOfWorkAsync(async () =>
            {
                await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
                {
                    BuildMetalLine(metalId, quantity: 2m, amount: 4m, id: lineId),
                });
                return true;
            });

            var line = await _recipeLines.GetAsync(lineId);
            line.Origin.ShouldBe(RecipeLineOrigin.Template);
        }
    }

    [Fact]
    public async Task Reordering_alone_does_not_hand_a_template_line_over()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;
        var variantId = Guid.NewGuid();
        var firstMetalId = Guid.NewGuid();
        var secondMetalId = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            var firstId = await InsertTemplateMetalLineAsync(
                companyId, variantId, firstMetalId, quantity: 2m, amount: 4m, lineOrder: 0);
            var secondId = await InsertTemplateMetalLineAsync(
                companyId, variantId, secondMetalId, quantity: 5m, amount: 10m, lineOrder: 1);

            // Yalnız SIRA değişir (satırlar yer değiştirir); hiçbir kullanıcı alanı değişmez.
            await WithUnitOfWorkAsync(async () =>
            {
                await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
                {
                    BuildMetalLine(secondMetalId, quantity: 5m, amount: 10m, id: secondId, lineOrder: 0),
                    BuildMetalLine(firstMetalId, quantity: 2m, amount: 4m, id: firstId, lineOrder: 1),
                });
                return true;
            });

            var lines = await _recipeLines.GetListAsync(l => l.ProductVariantId == variantId);
            lines.Select(l => l.Origin).ShouldAllBe(o => o == RecipeLineOrigin.Template);
        }
    }

    [Fact]
    public async Task Re_applying_a_template_keeps_the_edited_line_and_refreshes_the_untouched_one()
    {
        var companyId = Guid.NewGuid();
        _companyContext.CompanyId = companyId;
        var variantId = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            var template = BuildTemplate(companyId, firstOperand: 12m, secondOperand: 20m);

            // Uygulayıcı çağrıları WithUnitOfWorkAsync İÇİNDE koşar: içeride GetQueryableAsync + IAsyncQueryableExecuter
            // kullanılıyor ve ambient UoW yokken her repository çağrısı kendi UoW'unu açıp KAPATIR — dönen
            // IQueryable'ın DbContext'i sorgu çalışmadan dispose olurdu (projedeki yerleşik test deseni).
            var firstApply = await WithUnitOfWorkAsync(
                async () => await _applier.ApplyToVariantAsync(companyId, variantId, template));
            firstApply.ShouldBe(0, "İlk uygulamada korunacak kullanıcı düzenlemesi yoktur.");

            var applied = await LoadLinesAsync(variantId);
            applied.Count.ShouldBe(2);
            applied.Select(l => l.Origin).ShouldAllBe(o => o == RecipeLineOrigin.Template);

            // Kullanıcı BİRİNCİ şablon satırının oranını düzeltir (12 → 99); ikinciye dokunmaz.
            await WithUnitOfWorkAsync(async () =>
            {
                await _writer.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto>
                {
                    BuildServiceLine(operand: 99m, id: applied[0].Id, lineOrder: 0),
                    BuildServiceLine(operand: 20m, id: applied[1].Id, lineOrder: 1),
                });
                return true;
            });

            // ŞABLON YENİDEN UYGULANIR — kullanıcının 99'u hayatta kalmalı, dokunulmamış satır tazelenmeli.
            var secondApply = await WithUnitOfWorkAsync(
                async () => await _applier.ApplyToVariantAsync(companyId, variantId, template));
            secondApply.ShouldBe(
                1,
                "Korunan düzenleme sayısı çağırana bildirilmeli: o kalem artık iki kez görünüyor (kullanıcının " +
                "sürümü + şablonun yeniden kurulan sürümü) ve bu SESSİZ kalırsa maliyet fark edilmeden şişer.");

            var after = await LoadLinesAsync(variantId);
            var owned = after.Where(l => l.Origin == RecipeLineOrigin.TemplateEdited).ToList();
            var refreshed = after.Where(l => l.Origin == RecipeLineOrigin.Template).ToList();

            owned.ShouldHaveSingleItem().DerivedOperand.ShouldBe(
                99m, "Kullanıcının düzenlediği satır yeniden uygulamada SİLİNMEMELİ.");
            refreshed.Select(l => l.DerivedOperand).OrderBy(o => o).ShouldBe(new[] { 12m, 20m });

            // Düzenlenen satır artık şablonun malı değil → onun yerine şablonun kendi satırı yeniden kuruldu.
            after.Count.ShouldBe(3);
        }
    }

    /// <summary>İki hizmet satırlı şablon — persist edilmez: uygulayıcı yalnız satır alanlarını okur, şablon
    /// kimliğine dokunmaz (test konusu sahiplenme, şablon CRUD'u değil).</summary>
    private static RecipeTemplate BuildTemplate(Guid companyId, decimal firstOperand, decimal secondOperand)
    {
        var template = new RecipeTemplate(companyId, "Sahiplenme Şablonu");
        template.AddLine(RecipeComponentType.Service, 0)
            .SetService(null, RecipeDerivedOperation.Percent, firstOperand, null, null);
        template.AddLine(RecipeComponentType.Service, 1)
            .SetService(null, RecipeDerivedOperation.Percent, secondOperand, null, null);
        return template;
    }

    /// <summary>Şablonun uyguladığı bir maden satırını doğrudan kurar (uygulayıcının çıktısının aynısı) —
    /// yazıcı testleri şablon CRUD'una bağlanmasın diye.</summary>
    private async Task<Guid> InsertTemplateMetalLineAsync(
        Guid companyId,
        Guid variantId,
        Guid metalId,
        decimal quantity,
        decimal amount,
        int lineOrder = 0)
    {
        var line = new ProductVariantRecipeLine(
            companyId, variantId, RecipeComponentType.CatalogCommodity, lineOrder);
        line.SetCatalogCommodity(
            ProcessType.Metal, metalId, null, quantity, amount, 0.916m, null, ProcessPaymentType.Normal, 0m, null);
        line.SetOrigin(RecipeLineOrigin.Template);
        await _recipeLines.InsertAsync(line, autoSave: true);
        return line.Id;
    }

    private async Task<List<ProductVariantRecipeLine>> LoadLinesAsync(Guid variantId)
    {
        var lines = await _recipeLines.GetListAsync(l => l.ProductVariantId == variantId);
        return lines.OrderBy(l => l.LineOrder).ToList();
    }

    private static ProductRecipeLineGraphDto BuildMetalLine(
        Guid metalId,
        decimal quantity,
        decimal amount,
        Guid id,
        int lineOrder = 0)
    {
        return new ProductRecipeLineGraphDto
        {
            Id = id,
            LineOrder = lineOrder,
            ComponentType = RecipeComponentType.CatalogCommodity,
            CommodityProcessType = ProcessType.Metal,
            CommodityId = metalId,
            Quantity = quantity,
            Amount = amount,
            Factor = 0.916m,
        };
    }

    private static ProductRecipeLineGraphDto BuildServiceLine(decimal operand, Guid id, int lineOrder)
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
}
