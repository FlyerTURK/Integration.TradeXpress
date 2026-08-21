using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Vouchers;
using Microsoft.Extensions.Localization;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// <see cref="ProductSaleValidator"/> SAF kural testleri (DB'siz; snapshot elle kurulur). Her issue kodu için en az
/// bir fact; KDV'nin ASLA Error olmadığı AYRI bir fact'le çivili (2026-08-19 Hakan kararı). KIRMIZIYSA kural
/// ya sessizce değişmiş ya da kod sözleşmesi (Code) bozulmuştur — testi gevşetme, kuralı/kodu düzelt.
/// </summary>
public class ProductSaleValidatorTests
{
    private readonly ProductSaleValidator _validator = new(new PassThroughLocalizer());

    // ── Ürün kuralları ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Missing_category_is_an_error_on_the_general_tab()
    {
        var verdict = _validator.Validate(Snapshot(hasCategory: false));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductNoCategory);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Error);
        issue.FixTarget.ShouldBe(SaleReadinessFixTarget.GeneralTab);
        issue.StepKey.ShouldBe(ProductSaleValidator.StepCategory);
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepCategory).State.ShouldBe(SaleReadinessStepState.Blocked);
        verdict.CanVerify.ShouldBeFalse();   // ürün-düzeyi Error → hiçbir varyant doğrulanamaz
    }

    /// <summary>KDV: YALNIZ BİLGİ — Hakan 2026-08-19 ("KDV'nin sistemimizde çok da önemi yoktur"; aynı gün ikinci
    /// karar: Warning bile fazlaydı — Kategori adımına ünlem düşürüp ayrı bir sorun sanılıyordu). Bu fact ayrı
    /// durur: bir gün biri "push KDV istiyor" diye Warning/Error'a çevirmeye kalkarsa burada kırmızı görür.
    /// Info olduğu için adım durumu ve issue sayacı da etkilenmez (Step fact'i ayrıca pinler).</summary>
    [Fact]
    public void Missing_vat_is_never_an_error()
    {
        var verdict = _validator.Validate(Snapshot(vatRate: null));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductVatMissing);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Info);
        issue.Severity.ShouldNotBe(SaleReadinessSeverity.Error);
        verdict.CanVerify.ShouldBeTrue();
        verdict.HasBlockingProductIssue().ShouldBeFalse();

        // Kategori adımı KDV yüzünden "Dikkat"e DÜŞMEZ ve issue saymaz (2026-08-19 canlı TEST ürünü bulgusu).
        var categoryStep = verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepCategory);
        categoryStep.State.ShouldBe(SaleReadinessStepState.Done);
        categoryStep.IssueCount.ShouldBe(0);
    }

    [Fact]
    public void Vat_present_produces_no_vat_issue()
    {
        var verdict = _validator.Validate(Snapshot(vatRate: 20));
        verdict.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ProductVatMissing);
    }

    [Fact]
    public void Missing_image_is_a_warning_on_the_media_tab()
    {
        var verdict = _validator.Validate(Snapshot(imageCount: 0));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductNoImage);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Warning);
        issue.FixTarget.ShouldBe(SaleReadinessFixTarget.MediaTab);
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepImages).State.ShouldBe(SaleReadinessStepState.NotStarted);
        verdict.CanVerify.ShouldBeTrue();
    }

    /// <summary>REÇETE ŞABLONU SEÇİLMEDİ — 2026-08-20 Hakan kararı: şablon seçimi zorunludur ama zorunluluk
    /// "veritabanı seviyesinde olmasın"; kolon nullable kalır, uyarı satışa hazırlık panelinde yaşar. Bu fact ağırlığı ÇİVİLER:
    /// bir gün biri "zorunluysa Error olsun" diye çevirmeye kalkarsa, kod <c>Product:*</c> olduğu için ürünün
    /// TÜM varyantlarının doğrulanması dururdu — talimatın ötesine geçen bir kilit.</summary>
    [Fact]
    public void Missing_recipe_template_is_a_warning_that_does_not_block_verification()
    {
        var verdict = _validator.Validate(Snapshot(hasRecipeTemplate: false));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductNoRecipeTemplate);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Warning);
        issue.Severity.ShouldNotBe(SaleReadinessSeverity.Error);
        issue.StepKey.ShouldBe(ProductSaleValidator.StepRecipe);
        issue.FixTarget.ShouldBe(SaleReadinessFixTarget.GeneralTab);
        issue.Path.ShouldBe(SaleReadinessScope.General);

        // KİLİTLEMEDİĞİ ayrıca çivili: ürün-düzeyi engel yok, doğrulama açık, adım yalnız "Dikkat".
        verdict.HasBlockingProductIssue().ShouldBeFalse();
        verdict.CanVerify.ShouldBeTrue();
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepRecipe).State.ShouldBe(SaleReadinessStepState.Attention);
    }

    [Fact]
    public void Selected_recipe_template_produces_no_issue()
    {
        var verdict = _validator.Validate(Snapshot(hasRecipeTemplate: true));
        verdict.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ProductNoRecipeTemplate);
    }

    [Fact]
    public void Passive_product_is_a_warning()
    {
        var verdict = _validator.Validate(Snapshot(isActive: false));

        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductPassive).Severity.ShouldBe(SaleReadinessSeverity.Warning);
        verdict.CanVerify.ShouldBeTrue();
    }

    /// <summary>KARIŞIK PARA BİRİMİ (2026-08-21 parite borcu erimesi): fiyatlı varyantlar farklı birimlerdeyse
    /// pazaryeri push'u kesilir (Trendyol MixedCurrency her karışımda, N11 kanal/ürün birimi boşken) — panel bunu
    /// önceden Warning olarak gösterir. WARNING ÇİVİLİ, Error DEĞİL: push zaten kesiyor; Error olsaydı doğrulamayı
    /// da kilitleyip aynı engeli iki kez kurardı.</summary>
    [Fact]
    public void Mixed_variant_price_currencies_are_a_warning_that_does_not_block_verification()
    {
        var tl = Variant(code: "TL", saleStatus: ProductSaleStatus.Ready, currencyUnitId: Guid.NewGuid());
        var usd = Variant(code: "USD", saleStatus: ProductSaleStatus.Ready, currencyUnitId: Guid.NewGuid());
        var verdict = _validator.Validate(Snapshot(variants: new[] { tl, usd }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductMixedCurrency);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Warning);
        issue.Severity.ShouldNotBe(SaleReadinessSeverity.Error);
        issue.StepKey.ShouldBe(ProductSaleValidator.StepVariants);
        issue.FixTarget.ShouldBe(SaleReadinessFixTarget.VariantsTab);
        issue.Path.ShouldBe(SaleReadinessScope.Variants);
        verdict.CanVerify.ShouldBeTrue();
    }

    [Fact]
    public void A_single_shared_price_currency_produces_no_mixed_currency_issue()
    {
        var currency = Guid.NewGuid();
        var a = Variant(code: "A", saleStatus: ProductSaleStatus.Ready, currencyUnitId: currency);
        var b = Variant(code: "B", saleStatus: ProductSaleStatus.Ready, currencyUnitId: currency);
        var verdict = _validator.Validate(Snapshot(variants: new[] { a, b }));

        verdict.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ProductMixedCurrency);
    }

    /// <summary>Fiyatsız varyantın birimi push satırına girmez (o satırın kendi fail-fast'i Variant:NoSalePrice) —
    /// karışıklık sayımına da girmez; girseydi fiyat eksikliği bir de sahte "karışık birim" uyarısı doğururdu.</summary>
    [Fact]
    public void An_unpriced_variant_currency_does_not_count_toward_the_mix()
    {
        var priced = Variant(code: "A", saleStatus: ProductSaleStatus.Ready, currencyUnitId: Guid.NewGuid());
        var unpriced = Variant(code: "B", salePrice: null, currencyUnitId: Guid.NewGuid());
        var verdict = _validator.Validate(Snapshot(variants: new[] { priced, unpriced }));

        verdict.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ProductMixedCurrency);
    }

    [Fact]
    public void Calculated_product_without_tracked_commodity_line_is_an_error()
    {
        // Yalnız hizmet satırı: Calculated'ın stok zinciri veri bulamaz.
        var variant = Variant(lines: new[] { ServiceLine(0) });
        var verdict = _validator.Validate(Snapshot(stockPolicy: ProductStockPolicy.Calculated, variants: new[] { variant }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductCalculatedWithoutTrackedCommodity);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Error);
        issue.FixTarget.ShouldBe(SaleReadinessFixTarget.VariantsTab);
        verdict.CanVerify.ShouldBeFalse();
    }

    [Fact]
    public void Calculated_product_with_a_metal_line_passes_the_tracked_commodity_rule()
    {
        var variant = Variant(lines: new[] { CatalogLine(0, ProcessType.Metal, quantity: 0m, amount: 5m) });
        var verdict = _validator.Validate(Snapshot(stockPolicy: ProductStockPolicy.Calculated, variants: new[] { variant }));

        verdict.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ProductCalculatedWithoutTrackedCommodity);
        verdict.CanVerify.ShouldBeTrue();
    }

    [Fact]
    public void Product_without_active_variants_is_an_error_and_cannot_be_verified()
    {
        var verdict = _validator.Validate(Snapshot(variants: Array.Empty<ProductSaleVariantSnapshot>()));

        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductNoActiveVariant).Severity.ShouldBe(SaleReadinessSeverity.Error);
        verdict.CanVerify.ShouldBeFalse();
    }

    // ── Varyant kuralları ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Unpriced_variant_is_an_error_targeting_the_variant_form()
    {
        var priced = Variant(code: "RED", salePrice: 100m);
        var unpriced = Variant(code: "BLUE", salePrice: null);
        var verdict = _validator.Validate(Snapshot(variants: new[] { priced, unpriced }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.VariantNoSalePrice);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Error);
        issue.FixTarget.ShouldBe(SaleReadinessFixTarget.VariantForm);
        issue.TargetId.ShouldBe(unpriced.VariantId);
        issue.TargetLabel.ShouldBe("BLUE");

        verdict.HasBlockingVariantIssue(unpriced.VariantId).ShouldBeTrue();
        verdict.HasBlockingVariantIssue(priced.VariantId).ShouldBeFalse();
        verdict.CanVerify.ShouldBeTrue();   // fiyatlı kardeş doğrulanabilir
        verdict.PricedVariantCount.ShouldBe(1);
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepVariants).State.ShouldBe(SaleReadinessStepState.Blocked);
    }

    [Fact]
    public void Missing_recipe_is_an_error_for_calculated_but_a_warning_for_fixed()
    {
        var bare = Variant(lines: Array.Empty<ProductSaleRecipeLineSnapshot>());

        var fixedVerdict = _validator.Validate(Snapshot(stockPolicy: ProductStockPolicy.Fixed, variants: new[] { bare }));
        fixedVerdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.VariantNoRecipe).Severity.ShouldBe(SaleReadinessSeverity.Warning);
        fixedVerdict.CanVerify.ShouldBeTrue();

        var unlimitedVerdict = _validator.Validate(Snapshot(stockPolicy: ProductStockPolicy.Unlimited, variants: new[] { bare }));
        unlimitedVerdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.VariantNoRecipe).Severity.ShouldBe(SaleReadinessSeverity.Warning);

        var calculatedVerdict = _validator.Validate(Snapshot(stockPolicy: ProductStockPolicy.Calculated, variants: new[] { bare }));
        calculatedVerdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.VariantNoRecipe).Severity.ShouldBe(SaleReadinessSeverity.Error);
        calculatedVerdict.CanVerify.ShouldBeFalse();
    }

    /// <summary>Katalog emtiası satırında 0 adet + 0 miktar = Error (2026-08-19 Hakan kuralı; kuralın tek kaynağı
    /// <see cref="RecipeLineQuantityRule"/>). Hizmet satırı kapsam dışı (adedi/miktarı yoktur).</summary>
    [Fact]
    public void Zero_quantity_catalog_line_is_an_error_but_zero_service_line_is_not()
    {
        var variant = Variant(lines: new[]
        {
            CatalogLine(0, ProcessType.Metal, quantity: 0m, amount: 0m),
            ServiceLine(1),
        });

        var verdict = _validator.Validate(Snapshot(variants: new[] { variant }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.RecipeZeroQuantity);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Error);
        issue.TargetId.ShouldBe(variant.VariantId);
        issue.FixTarget.ShouldBe(SaleReadinessFixTarget.VariantForm);
        verdict.CanVerify.ShouldBeFalse();
    }

    [Fact]
    public void Catalog_line_with_only_quantity_or_only_amount_is_satisfied()
    {
        var variant = Variant(lines: new[]
        {
            CatalogLine(0, ProcessType.Metal, quantity: 2m, amount: 0m),
            CatalogLine(1, ProcessType.Good, quantity: 0m, amount: 7m),
        });

        var verdict = _validator.Validate(Snapshot(variants: new[] { variant }));
        verdict.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.RecipeZeroQuantity);
    }

    [Fact]
    public void Draft_variant_is_an_info_pointing_to_verify()
    {
        var variant = Variant(saleStatus: ProductSaleStatus.Draft);
        var verdict = _validator.Validate(Snapshot(variants: new[] { variant }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.VariantNotVerified);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Info);
        issue.FixTarget.ShouldBe(SaleReadinessFixTarget.Verify);
        verdict.DraftVariantCount.ShouldBe(1);
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepVerification).State.ShouldBe(SaleReadinessStepState.NotStarted);
    }

    /// <summary>Rozet "Hazır" ama GUARD kapalı (<c>VerifiedRecipeStamp</c> bayat) — satışa hazırlık panelinin var olma sebebi.</summary>
    [Fact]
    public void Ready_variant_outside_the_gate_is_a_stale_verification_warning()
    {
        var stale = Variant(saleStatus: ProductSaleStatus.Ready);
        var verdict = _validator.Validate(Snapshot(variants: new[] { stale }, sellable: Array.Empty<Guid>()));

        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.VariantVerificationStale).Severity.ShouldBe(SaleReadinessSeverity.Warning);
        verdict.StaleVerifiedVariantCount.ShouldBe(1);
        verdict.SellableVariantCount.ShouldBe(0);
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepVerification).State.ShouldBe(SaleReadinessStepState.Attention);
    }

    [Fact]
    public void Ready_variant_inside_the_gate_is_clean_and_verification_step_is_done()
    {
        var ready = Variant(saleStatus: ProductSaleStatus.Ready);
        var verdict = _validator.Validate(Snapshot(variants: new[] { ready }, sellable: new[] { ready.VariantId }));

        verdict.Issues.ShouldNotContain(i => ProductSaleValidator.IsVariantScoped(i.Code));
        verdict.SellableVariantCount.ShouldBe(1);
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepVerification).State.ShouldBe(SaleReadinessStepState.Done);
    }

    [Fact]
    public void Suspended_variant_is_a_warning_pointing_to_verify()
    {
        var suspended = Variant(saleStatus: ProductSaleStatus.Suspended);
        var verdict = _validator.Validate(Snapshot(variants: new[] { suspended }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.VariantSuspended);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Warning);
        issue.FixTarget.ShouldBe(SaleReadinessFixTarget.Verify);
        verdict.SuspendedVariantCount.ShouldBe(1);
    }

    // ── Kanal kuralları ───────────────────────────────────────────────────────────────────────────────

    /// <summary>HİÇ KANAL ÜRÜNÜ YOK = ERROR (2026-08-19 Hakan talimatı: sekme KIRMIZI + toolbar üstünde uyarı).
    /// İlk sürümde Info'ydu ve palet Info'yu renklendirmediği için sekme sessiz kalıyordu — canlı TEST ürününde
    /// yakalandı. Bu fact iki şeyi birden çiviler: ağırlık Error (sekme renklenir) VE doğrulama ENGELLENMEZ
    /// (kanal eksikliği push'u ilgilendirir, insan onayını değil).</summary>
    [Fact]
    public void No_channel_product_is_an_error_that_still_allows_verification()
    {
        var verdict = _validator.Validate(Snapshot(channels: Array.Empty<ProductSaleChannelSnapshot>()));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ChannelNone);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Error);
        issue.FixTarget.ShouldBe(SaleReadinessFixTarget.ChannelsTab);

        // Kanal adımı ENGELLİ (kırmızı); push adımı hâlâ "yapılmadı" — gönderilecek kanal kaydı yok.
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepChannelProducts).State.ShouldBe(SaleReadinessStepState.Blocked);
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepPush).State.ShouldBe(SaleReadinessStepState.NotStarted);

        // Kanal issue'su ürün/varyant kapsamlı DEĞİLDİR → doğrulama yolu açık kalır.
        verdict.CanVerify.ShouldBeTrue();
        verdict.HasBlockingProductIssue().ShouldBeFalse();
    }

    [Fact]
    public void Unpushed_channel_product_is_an_info()
    {
        var verdict = _validator.Validate(Snapshot(channels: new[] { Channel(isListed: false) }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ChannelNotPushed);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Info);
        issue.FixTarget.ShouldBe(SaleReadinessFixTarget.ChannelProductForm);
        issue.ChannelType.ShouldBe(SalesChannelType.TrTrendyol);
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepPush).State.ShouldBe(SaleReadinessStepState.NotStarted);
    }

    [Fact]
    public void Pending_channel_product_is_an_info_and_not_also_unpushed()
    {
        var verdict = _validator.Validate(Snapshot(channels: new[] { Channel(isListed: false, isPending: true) }));

        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ChannelPending).Severity.ShouldBe(SaleReadinessSeverity.Info);
        verdict.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ChannelNotPushed);
    }

    [Fact]
    public void Stale_batch_is_a_warning()
    {
        var verdict = _validator.Validate(Snapshot(channels: new[] { Channel(isStale: true) }));
        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ChannelStale).Severity.ShouldBe(SaleReadinessSeverity.Warning);
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepPush).State.ShouldBe(SaleReadinessStepState.Attention);
    }

    [Fact]
    public void Last_error_is_a_warning_carrying_the_channel_sentence()
    {
        var verdict = _validator.Validate(Snapshot(channels: new[] { Channel(lastError: "Barkod zaten kayıtlı") }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ChannelLastError);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Warning);
        issue.Message.ShouldContain("Barkod zaten kayıtlı");
    }

    [Fact]
    public void Passive_channel_product_is_a_warning()
    {
        var verdict = _validator.Validate(Snapshot(channels: new[] { Channel(isActive: false) }));
        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ChannelPassive).Severity.ShouldBe(SaleReadinessSeverity.Warning);
    }

    [Fact]
    public void Obstacle_is_a_warning_carrying_the_obstacle_text()
    {
        var verdict = _validator.Validate(Snapshot(channels: new[] { Channel(obstacle: "Kara liste") }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ChannelObstacle);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Warning);
        issue.Message.ShouldContain("Kara liste");
    }

    /// <summary>Kanal Error'ı PUSH'u engeller, DOĞRULAMAYI DEĞİL — CanVerify etkilenmez.</summary>
    [Fact]
    public void Missing_required_channel_fields_is_an_error_that_does_not_block_verification()
    {
        var channel = Channel(missingRequiredFields: true);
        var verdict = _validator.Validate(Snapshot(channels: new[] { channel }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ChannelMissingRequiredFields);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Error);
        issue.TargetId.ShouldBe(channel.ChannelProductId);
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepChannelProducts).State.ShouldBe(SaleReadinessStepState.Blocked);
        verdict.CanVerify.ShouldBeTrue();
    }

    // ── Derin issue: kanal ürünü var ama varyantta temel emtia yok ────────────────────────────────────

    /// <summary>Hakan senaryosu (2026-08-19): <i>"kanal ürünü var ama varyantlara temel emtia eklenmemiş"</i>.
    /// Ağırlık, core <c>Variant:NoRecipe</c> ile aynı felsefede: Calculated'da reçete stoğun KAYNAĞIdır → Error.
    /// Yol kanal ürününden varyant reçetesine kadar iner — tek issue beş bölümü birden işaretlesin.</summary>
    [Fact]
    public void Channel_variant_without_a_catalog_commodity_is_an_error_for_calculated()
    {
        // Yalnız işçilik satırı: "reçetesi var" görünür ama satacak emtiası yoktur.
        var variant = Variant(code: "22A", lines: new[] { ServiceLine(0) });
        var channel = Channel(isListed: true);
        var verdict = _validator.Validate(Snapshot(
            stockPolicy: ProductStockPolicy.Calculated,
            variants: new[] { variant },
            channels: new[] { channel }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ChannelVariantWithoutCommodity);
        issue.Severity.ShouldBe(SaleReadinessSeverity.Error);
        issue.StepKey.ShouldBe(ProductSaleValidator.StepChannelProducts);
        issue.FixTarget.ShouldBe(SaleReadinessFixTarget.ChannelProductForm);
        issue.TargetId.ShouldBe(channel.ChannelProductId);       // düzeltme kanal ürünü formunda yapılır
        issue.TargetLabel.ShouldBe("22A");                       // ama okunması gereken HANGİ VARYANT olduğudur
        issue.ChannelType.ShouldBe(SalesChannelType.TrTrendyol);
        issue.Path.ShouldBe(SaleReadinessScope.ChannelVariantRecipe(channel.ChannelProductId, variant.VariantId));

        // Kanal Error'ı push'u engeller, doğrulamayı DEĞİL (Channel:* kodu ne ürün ne varyant kapsamlıdır).
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepChannelProducts).State.ShouldBe(SaleReadinessStepState.Blocked);
    }

    [Fact]
    public void Channel_variant_without_a_catalog_commodity_is_only_a_warning_for_fixed()
    {
        var variant = Variant(lines: Array.Empty<ProductSaleRecipeLineSnapshot>());
        var verdict = _validator.Validate(Snapshot(
            stockPolicy: ProductStockPolicy.Fixed,
            variants: new[] { variant },
            channels: new[] { Channel(isListed: true) }));

        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ChannelVariantWithoutCommodity)
            .Severity.ShouldBe(SaleReadinessSeverity.Warning);
    }

    /// <summary>KAPATILAMAYAN UYARI ÜRETİLMEZ: sınıflandırma sihirbazı yalnız hizmet satırı taşıyan ürünü BİLEREK
    /// <c>Unlimited</c> yapar (CLAUDE.md §6 — Calculated stok zincirinin veri bulamayacağı bir hesap açardı).
    /// Böyle bir üründe "temel emtia ekle" demek, sihirbazın bilerek açmadığı katalog kaydını açmak demektir →
    /// issue doğmaz ve kanal adımı temiz kalır. Bu fact olmasaydı meşru bir yapılandırma kalıcı uyarıya düşerdi.</summary>
    [Fact]
    public void Service_only_unlimited_product_produces_no_channel_commodity_issue()
    {
        var variant = Variant(saleStatus: ProductSaleStatus.Ready, lines: new[] { ServiceLine(0) });
        var verdict = _validator.Validate(Snapshot(
            stockPolicy: ProductStockPolicy.Unlimited,
            variants: new[] { variant },
            channels: new[] { Channel(isListed: true) }));

        verdict.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ChannelVariantWithoutCommodity);
        verdict.Steps.Single(s => s.Key == ProductSaleValidator.StepChannelProducts).State.ShouldBe(SaleReadinessStepState.Done);
    }

    /// <summary>Muafiyet POLİTİKAYA bağlıdır, "hizmet satırı var mı"ya değil: <c>Unlimited</c> üründe reçetesi
    /// tamamen boş varyant da kanal issue'su doğurmaz — eksik reçeteyi zaten core <c>Variant:NoRecipe</c>
    /// söyler ve onu kanal bölümüne ikinci kez taşımak stoğu reçeteden türemeyen üründe bilgi katmaz.</summary>
    [Fact]
    public void An_unlimited_product_is_exempt_by_policy_not_by_its_service_line()
    {
        var verdict = _validator.Validate(Snapshot(
            stockPolicy: ProductStockPolicy.Unlimited,
            variants: new[] { Variant(lines: Array.Empty<ProductSaleRecipeLineSnapshot>()) },
            channels: new[] { Channel(isListed: true) }));

        verdict.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ChannelVariantWithoutCommodity);
        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.VariantNoRecipe)
            .Severity.ShouldBe(SaleReadinessSeverity.Warning);
    }

    /// <summary>Katalog satırı VARSA issue doğmaz — ölçüt satır sayısı değil, KATALOG satırının varlığıdır.</summary>
    [Fact]
    public void A_variant_with_a_catalog_line_produces_no_channel_commodity_issue()
    {
        var variant = Variant(lines: new[]
        {
            CatalogLine(0, ProcessType.Metal, quantity: 0m, amount: 5m),
            ServiceLine(1),
        });

        var verdict = _validator.Validate(Snapshot(
            stockPolicy: ProductStockPolicy.Calculated,
            variants: new[] { variant },
            channels: new[] { Channel(isListed: true) }));

        verdict.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ChannelVariantWithoutCommodity);
    }

    /// <summary>Kanal ürünü YOKSA derin issue da yoktur: kural "kanala aday ürün" hakkındadır, boş yere kanal
    /// sekmesini kırmızıya boyamaz (eksik reçeteyi zaten <c>Variant:NoRecipe</c> söylüyor).</summary>
    [Fact]
    public void Without_a_channel_product_there_is_no_deep_commodity_issue()
    {
        var variant = Variant(lines: new[] { ServiceLine(0) });
        var verdict = _validator.Validate(Snapshot(
            stockPolicy: ProductStockPolicy.Calculated,
            variants: new[] { variant },
            channels: Array.Empty<ProductSaleChannelSnapshot>()));

        verdict.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ChannelVariantWithoutCommodity);
    }

    /// <summary>Kanal × varyant başına TEK issue — iki kanal ürünü ve iki emtiasız varyant dört satır üretir ve
    /// her satır kendi kanalının/varyantının yolunu taşır (yollar çakışırsa iki bölüm aynı issue'yu paylaşırdı).</summary>
    [Fact]
    public void One_issue_per_channel_and_variant_pair()
    {
        var bare1 = Variant(code: "A", lines: Array.Empty<ProductSaleRecipeLineSnapshot>());
        var bare2 = Variant(code: "B", lines: new[] { ServiceLine(0) });
        var channel1 = Channel(isListed: true);
        var channel2 = Channel(isListed: true);

        var verdict = _validator.Validate(Snapshot(
            variants: new[] { bare1, bare2 },
            channels: new[] { channel1, channel2 }));

        var paths = verdict.Issues
            .Where(i => i.Code == ProductSaleValidator.ChannelVariantWithoutCommodity)
            .Select(i => i.Path)
            .ToList();

        paths.Count.ShouldBe(4);
        paths.Distinct().Count().ShouldBe(4);
        paths.ShouldContain(SaleReadinessScope.ChannelVariantRecipe(channel1.ChannelProductId, bare1.VariantId));
        paths.ShouldContain(SaleReadinessScope.ChannelVariantRecipe(channel2.ChannelProductId, bare2.VariantId));
    }

    // ── Kapsam yolları (SaleReadinessScope) ───────────────────────────────────────────────────────────

    /// <summary>Yol SÖZLEŞMEDİR: her bölüm "benim yolumla başlayan issue'ların en yüksek ağırlığı" diye sorar.
    /// Bir issue'nun yolu kayarsa (ör. varyant fiyatı reçete kapsamına düşerse) o issue YANLIŞ bölümü boyar ve
    /// doğru bölüm temiz görünür — bu yüzden her seviye ayrı ayrı çivilenir.</summary>
    [Fact]
    public void Product_general_issues_carry_the_general_scope()
    {
        var verdict = _validator.Validate(Snapshot(hasCategory: false, vatRate: null, isActive: false));

        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductNoCategory)
            .Path.ShouldBe(SaleReadinessScope.General);
        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductVatMissing)
            .Path.ShouldBe(SaleReadinessScope.General);
        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductPassive)
            .Path.ShouldBe(SaleReadinessScope.General);
    }

    [Fact]
    public void Missing_image_carries_the_media_scope()
    {
        var verdict = _validator.Validate(Snapshot(imageCount: 0));

        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductNoImage)
            .Path.ShouldBe(SaleReadinessScope.Media);
    }

    /// <summary>Ürün geneli doğan ama düzeltmesi varyant reçetelerinde olan iki issue VARYANTLAR kapsamındadır —
    /// kullanıcıyı düzeltemeyeceği "Genel" sekmesine göndermek yanlış yönlendirme olurdu.</summary>
    [Fact]
    public void Product_level_variant_issues_carry_the_variants_scope()
    {
        var emptyVerdict = _validator.Validate(Snapshot(variants: Array.Empty<ProductSaleVariantSnapshot>()));
        emptyVerdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductNoActiveVariant)
            .Path.ShouldBe(SaleReadinessScope.Variants);

        var serviceOnly = Variant(lines: new[] { ServiceLine(0) });
        var calculatedVerdict = _validator.Validate(Snapshot(
            stockPolicy: ProductStockPolicy.Calculated, variants: new[] { serviceOnly }));
        calculatedVerdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ProductCalculatedWithoutTrackedCommodity)
            .Path.ShouldBe(SaleReadinessScope.Variants);
    }

    [Fact]
    public void Unpriced_variant_carries_the_variant_scope_but_not_the_recipe_scope()
    {
        var unpriced = Variant(salePrice: null);
        var verdict = _validator.Validate(Snapshot(variants: new[] { unpriced }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.VariantNoSalePrice);
        issue.Path.ShouldBe(SaleReadinessScope.Variant(unpriced.VariantId));
        SaleReadinessScope.IsWithin(issue.Path, SaleReadinessScope.Variants).ShouldBeTrue();
        SaleReadinessScope.IsWithin(issue.Path, SaleReadinessScope.VariantRecipe(unpriced.VariantId)).ShouldBeFalse();
    }

    [Fact]
    public void Recipe_issues_carry_the_variant_recipe_scope()
    {
        var bare = Variant(lines: Array.Empty<ProductSaleRecipeLineSnapshot>());
        var noRecipeVerdict = _validator.Validate(Snapshot(variants: new[] { bare }));
        noRecipeVerdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.VariantNoRecipe)
            .Path.ShouldBe(SaleReadinessScope.VariantRecipe(bare.VariantId));

        var zero = Variant(lines: new[] { CatalogLine(0, ProcessType.Metal, quantity: 0m, amount: 0m) });
        var zeroVerdict = _validator.Validate(Snapshot(variants: new[] { zero }));
        var zeroIssue = zeroVerdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.RecipeZeroQuantity);
        zeroIssue.Path.ShouldBe(SaleReadinessScope.VariantRecipe(zero.VariantId));

        // Reçete issue'su VARYANT kapsamının da içindedir — varyant satırı da işaretlensin.
        SaleReadinessScope.IsWithin(zeroIssue.Path, SaleReadinessScope.Variant(zero.VariantId)).ShouldBeTrue();
    }

    /// <summary>Doğrulama issue'larının yolu varyant DEĞİL doğrulama kapsamıdır: düzeltme yeri satışa hazırlık
    /// panelinin "Doğrula" düğmesidir. TargetId varyant kalır (hangi kayıt olduğu okunsun) ama yol, bölümü yanlış boyamaz.</summary>
    [Fact]
    public void Verification_issues_carry_the_verification_scope_while_targeting_the_variant()
    {
        var suspended = Variant(saleStatus: ProductSaleStatus.Suspended);
        var verdict = _validator.Validate(Snapshot(variants: new[] { suspended }));

        var issue = verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.VariantSuspended);
        issue.Path.ShouldBe(SaleReadinessScope.Verification);
        issue.TargetId.ShouldBe(suspended.VariantId);
        SaleReadinessScope.IsWithin(issue.Path, SaleReadinessScope.Variants).ShouldBeFalse();
    }

    [Fact]
    public void No_channel_product_carries_the_channels_scope()
    {
        var verdict = _validator.Validate(Snapshot(channels: Array.Empty<ProductSaleChannelSnapshot>()));

        verdict.Issues.ShouldHaveSingleItemWithCode(ProductSaleValidator.ChannelNone)
            .Path.ShouldBe(SaleReadinessScope.Channels);
    }

    [Fact]
    public void Channel_scoped_issues_carry_that_channel_products_scope()
    {
        var channel = Channel(missingRequiredFields: true, isActive: false, lastError: "Barkod zaten kayıtlı");
        var other = Channel(isListed: true);
        var verdict = _validator.Validate(Snapshot(channels: new[] { channel, other }));

        var expected = SaleReadinessScope.Channel(channel.ChannelProductId);
        foreach (var code in new[]
                 {
                     ProductSaleValidator.ChannelMissingRequiredFields,
                     ProductSaleValidator.ChannelPassive,
                     ProductSaleValidator.ChannelLastError,
                 })
        {
            verdict.Issues.ShouldHaveSingleItemWithCode(code).Path.ShouldBe(expected);
        }

        // Kanal issue'su KANALLAR sekmesinin de içindedir, ama KOMŞU kanal ürününün içinde DEĞİLDİR.
        SaleReadinessScope.IsWithin(expected, SaleReadinessScope.Channels).ShouldBeTrue();
        SaleReadinessScope.IsWithin(expected, SaleReadinessScope.Channel(other.ChannelProductId)).ShouldBeFalse();
    }

    /// <summary>Hiçbir issue YOLSUZ doğmaz — yolsuz issue hiçbir bölümün kapsamına düşmez ve sessizce kaybolur.
    /// Yeni bir kural eklenirken yol unutulursa burası kırmızı olur.</summary>
    [Fact]
    public void Every_issue_carries_a_scope_path()
    {
        var bare = Variant(salePrice: null, saleStatus: ProductSaleStatus.Suspended,
            lines: Array.Empty<ProductSaleRecipeLineSnapshot>());

        var verdict = _validator.Validate(Snapshot(
            hasCategory: false,
            vatRate: null,
            isActive: false,
            imageCount: 0,
            stockPolicy: ProductStockPolicy.Calculated,
            variants: new[] { bare },
            channels: new[] { Channel(isActive: false, isListed: false, lastError: "hata", obstacle: "Kara liste", missingRequiredFields: true) }));

        verdict.Issues.ShouldNotBeEmpty();
        verdict.Issues.ShouldAllBe(i => !string.IsNullOrWhiteSpace(i.Path));
    }

    // ── Adımlar / sıralama ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Steps_come_in_the_fixed_order_and_a_clean_product_is_all_done()
    {
        var ready = Variant(saleStatus: ProductSaleStatus.Ready);
        var verdict = _validator.Validate(Snapshot(
            vatRate: 20,
            imageCount: 2,
            variants: new[] { ready },
            sellable: new[] { ready.VariantId },
            channels: new[] { Channel(isListed: true) }));

        verdict.Steps.Select(s => s.Key).ShouldBe(new[]
        {
            ProductSaleValidator.StepCategory,
            ProductSaleValidator.StepVariants,
            ProductSaleValidator.StepRecipe,
            ProductSaleValidator.StepImages,
            ProductSaleValidator.StepVerification,
            ProductSaleValidator.StepChannelProducts,
            ProductSaleValidator.StepPush,
        });
        verdict.Steps.ShouldAllBe(s => s.State == SaleReadinessStepState.Done);
        verdict.Issues.ShouldBeEmpty();
        verdict.CanVerify.ShouldBeTrue();
    }

    [Fact]
    public void Issues_are_ordered_most_severe_first()
    {
        var verdict = _validator.Validate(Snapshot(
            hasCategory: false,          // Error
            vatRate: null,               // Warning
            channels: Array.Empty<ProductSaleChannelSnapshot>()));   // Info

        verdict.Issues.Count.ShouldBeGreaterThanOrEqualTo(3);
        verdict.Issues.Select(i => (int)i.Severity).ShouldBeInOrder(SortDirection.Descending);
    }

    // ── snapshot kurucuları ───────────────────────────────────────────────────────────────────────────

    /// <summary>Varsayılan: TEMİZ ürün (kategori var, KDV var, reçete şablonu seçili, görsel var, fiyatlı +
    /// reçeteli + guard'dan geçen tek varyant, listelenmiş Trendyol kanal ürünü). Her fact yalnız sınadığı alanı
    /// bozar.</summary>
    private static ProductSaleSnapshot Snapshot(
        bool hasCategory = true,
        int? vatRate = 20,
        bool hasRecipeTemplate = true,
        bool isActive = true,
        int imageCount = 1,
        ProductStockPolicy stockPolicy = ProductStockPolicy.Fixed,
        IReadOnlyList<ProductSaleVariantSnapshot>? variants = null,
        IReadOnlyCollection<Guid>? sellable = null,
        IReadOnlyList<ProductSaleChannelSnapshot>? channels = null)
    {
        variants ??= new[] { Variant(saleStatus: ProductSaleStatus.Ready) };

        // sellable verilmediyse: Ready varyantlar guard'dan geçiyor varsayılır (temiz senaryo).
        var sellableSet = sellable is null
            ? variants.Where(v => v.SaleStatus == ProductSaleStatus.Ready).Select(v => v.VariantId).ToHashSet()
            : sellable.ToHashSet();

        channels ??= new[] { Channel(isListed: true) };

        return new ProductSaleSnapshot(
            ProductId: Guid.NewGuid(),
            ProductCode: "URN-1",
            IsActive: isActive,
            StockPolicy: stockPolicy,
            VariantMode: ProductVariantMode.MultiVariant,
            HasCategory: hasCategory,
            VatRate: vatRate,
            // Kimliğin DEĞERİ okunmaz, yalnız "seçilmiş mi" sorulur — kalıcı olmayan test id'si meşrudur.
            RecipeTemplateId: hasRecipeTemplate ? Guid.NewGuid() : null,
            ActiveVariants: variants,
            SellableVariantIds: sellableSet,
            ImageCount: imageCount,
            HasPoster: imageCount > 0,
            Channels: channels);
    }

    private static ProductSaleVariantSnapshot Variant(
        string code = "V1",
        decimal? salePrice = 100m,
        Guid? currencyUnitId = null,
        ProductSaleStatus saleStatus = ProductSaleStatus.Draft,
        IReadOnlyList<ProductSaleRecipeLineSnapshot>? lines = null)
    {
        lines ??= new[] { CatalogLine(0, ProcessType.Metal, quantity: 0m, amount: 5m) };
        return new ProductSaleVariantSnapshot(Guid.NewGuid(), code, salePrice, currencyUnitId, saleStatus, lines);
    }

    private static ProductSaleRecipeLineSnapshot CatalogLine(int order, ProcessType family, decimal quantity, decimal amount)
    {
        return new ProductSaleRecipeLineSnapshot(order, RecipeComponentType.CatalogCommodity, family, quantity, amount, null);
    }

    private static ProductSaleRecipeLineSnapshot ServiceLine(int order)
    {
        return new ProductSaleRecipeLineSnapshot(order, RecipeComponentType.Service, null, 0m, 0m, "İşçilik");
    }

    private static ProductSaleChannelSnapshot Channel(
        bool isActive = true,
        bool isListed = true,
        bool isPending = false,
        bool isStale = false,
        string? lastError = null,
        string? obstacle = null,
        bool missingRequiredFields = false)
    {
        return new ProductSaleChannelSnapshot(
            Guid.NewGuid(), SalesChannelType.TrTrendyol, "Trendyol · TY1",
            isActive, isListed, isPending, isStale, lastError, obstacle, missingRequiredFields);
    }

    /// <summary>Anahtarı + argümanları geri veren localizer — kural testinde metin değil KOD sınanır; metin
    /// lokalizasyon dosyasının işidir (parite testi ayrıca var).</summary>
    private sealed class PassThroughLocalizer : IStringLocalizer<TradeXpressResource>
    {
        public LocalizedString this[string name]
        {
            get { return new LocalizedString(name, name, resourceNotFound: false); }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                var rendered = name + " " + string.Join(" ", arguments.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture)));
                return new LocalizedString(name, rendered, resourceNotFound: false);
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return Array.Empty<LocalizedString>();
        }
    }
}

/// <summary>Shouldly yardımcıları — "bu kodla TAM BİR issue var" sorusu her fact'te sorulur.</summary>
internal static class SaleReadinessIssueAssertions
{
    public static SaleReadinessIssueDto ShouldHaveSingleItemWithCode(this IEnumerable<SaleReadinessIssueDto> issues, string code)
    {
        var matches = issues.Where(i => i.Code == code).ToList();
        matches.Count.ShouldBe(1, $"'{code}' kodlu bulgu sayısı");
        return matches[0];
    }
}
