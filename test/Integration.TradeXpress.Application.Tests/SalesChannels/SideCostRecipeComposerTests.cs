using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// <see cref="SideCostRecipeComposer"/> saf kurucu testleri (DB'siz) + kâr korunumu uçtan-uca matematiği
/// (<see cref="ProductRecipeCostCalculator"/> ile) — 2026-07-10 gider-satırları modeli. Bağlayıcı kullanıcı
/// kararları (DAVRANIŞ KİLİDİ — eski modelden AYNEN taşındı): kargo/paketleme SABİT tutar; GrossUp (komisyon)
/// satırları reçetenin EN SONUNDA AllAbove ile (sabit giderler de komisyona tabi — 1443,75 senaryosu);
/// varyant opt-in kalemleri (sigortalı gönderim genellemesi) varsayılan KAPALI; N11 efektif oran %23,004;
/// Etsy sabit kalemler USD → değerlemeyle çevrilir.
/// </summary>
public class SideCostRecipeComposerTests
{
    private static readonly Guid Try = Guid.NewGuid();
    private static readonly Guid Usd = Guid.NewGuid();
    private static readonly Guid CommissionServiceId = Guid.NewGuid();

    private readonly ProductRecipeCostCalculator _calculator = new();

    // Doğal birim → "1 birim = X ülke parası" (SATIŞ yönü; TRY ülke birimi).
    private static Dictionary<Guid, decimal> Sell()
    {
        return new Dictionary<Guid, decimal> { [Try] = 1m, [Usd] = 40m };
    }

    // ── Gider satırı kurucuları (testlerin ortak dili) ──────────────────────────────────────────────

    private static SideCostItem Item(
        SideCostKind kind,
        SideCostCalcMode calcMode,
        decimal value,
        Guid? currencyUnitId = null,
        Guid? serviceId = null,
        bool autoRate = false,
        bool isEnabled = true,
        int displayOrder = 0,
        bool requiresVariantOptIn = false,
        string? displayName = null)
    {
        return new SideCostItem(
            kind, displayName, calcMode, value, currencyUnitId, serviceId,
            SideCostPostingMode.CounterpartyAccount, accountId: null, subAccountId: null,
            autoRate, isEnabled, displayOrder, requiresVariantOptIn);
    }

    private static SideCostItem Fixed(SideCostKind kind, decimal amount, int order = 0, Guid? currencyUnitId = null, bool optIn = false, bool enabled = true)
    {
        return Item(kind, SideCostCalcMode.FixedAmount, amount, currencyUnitId, displayOrder: order, requiresVariantOptIn: optIn, isEnabled: enabled);
    }

    private static SideCostItem Commission(decimal value, int order = 0, bool autoRate = false, bool enabled = true, string? name = null)
    {
        return Item(SideCostKind.Commission, SideCostCalcMode.GrossUpPercent, value,
            serviceId: CommissionServiceId, autoRate: autoRate, displayOrder: order, isEnabled: enabled, displayName: name);
    }

    private static SideCostPlan Plan(params SideCostItem[] items)
    {
        return new SideCostPlan(items, ResolvedCommissionRate: null, VariantOptInEnabled: false);
    }

    /// <summary>Kullanıcının fiziki taban satırı (parasal katalog benzeri) — composer bunlara DOKUNMAZ.</summary>
    private static ProductRecipeLineGraphDto UserBaseLine()
    {
        return new ProductRecipeLineGraphDto
        {
            ComponentType = RecipeComponentType.CatalogCommodity,
            CommodityProcessType = ProcessType.Stone,
            LineOrder = 0,
        };
    }

    /// <summary>Composer'ın ürettiği Hizmet satırını calculator girdisine çevirir (RecipeCostPopulator'ın yaptığı
    /// projeksiyonun test-yerel eşleniği; taban satır sabit tutarla temsil edilir).</summary>
    private static RecipeLineCostInput ToInput(ProductRecipeLineGraphDto dto, decimal userBaseCost)
    {
        if (dto.ComponentType == RecipeComponentType.Service)
        {
            return new RecipeLineCostInput(
                RecipeComponentType.Service, Family: null,
                Quantity: 0m, Amount: 0m, Factor: 0m,
                IsQuantity: false, StableQuantity: 0m,
                PriceByQuantity: false, EntryPrice: 0m,
                NaturalUnitId: null,
                ProcessPaymentType.Normal, PayFactor: 0m, PayUnitId: dto.PayUnitId, LaborByQuantity: false,
                ManualAmount: null, ManualUnitId: null,
                DerivedBaseMode: dto.DerivedBaseMode, DerivedOperation: dto.DerivedOperation, DerivedOperand: dto.DerivedOperand);
        }

        // Kullanıcı taban satırı — parasal katalog: EntryPrice × 1 @ TRY = userBaseCost.
        return new RecipeLineCostInput(
            RecipeComponentType.CatalogCommodity, ProcessType.Stone,
            Quantity: 0m, Amount: 1m, Factor: 0m,
            IsQuantity: false, StableQuantity: 0m,
            PriceByQuantity: false, EntryPrice: userBaseCost,
            NaturalUnitId: Try,
            ProcessPaymentType.Normal, PayFactor: 0m, PayUnitId: null, LaborByQuantity: false,
            ManualAmount: null, ManualUnitId: null);
    }

    private RecipeCostResult Compute(List<ProductRecipeLineGraphDto> lines, decimal userBaseCost)
    {
        var inputs = lines.Where(l => !l.IsDeleted).OrderBy(l => l.LineOrder).Select(l => ToInput(l, userBaseCost)).ToList();
        return _calculator.Compute(inputs, Sell(), "TRY");
    }

    // ── (a) kâr korunumu: taban 1000 + sabit giderler 100 + marj %5 + komisyon %20 (DAVRANIŞ KİLİDİ) ──

    [Fact]
    public void Profit_is_preserved_when_commission_grossup_is_last()
    {
        // Taban 1000 + paketleme 60 + kargo 40 = 1100; komisyon %20 GrossUp EN SON → net = 1100 ÷ 0.8 = 1375.
        // Fiyat = net × (1 + %5) = 1443.75 (≡ (1100×1.05)/0.80). Satıcının eline geçen = fiyat × 0.8 = 1155 = 1100×1.05.
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, Plan(
            Fixed(SideCostKind.Packaging, 60m, order: 0),
            Fixed(SideCostKind.Cargo, 40m, order: 1),
            Commission(20m, order: 2)));

        var result = Compute(lines, userBaseCost: 1000m);
        result.AnyMissingRate.ShouldBeFalse();
        result.Net.ShouldBe(1375m);

        var price = DerivedPriceCalculator.Calculate(result.Net, 5m);
        price.ShouldBe(1443.75m);

        var sellerKeeps = price * 0.8m;
        sellerKeeps.ShouldBe(1155m);            // = (1000 + 100) × 1.05 — kâr korunur
        sellerKeeps.ShouldBe(1100m * 1.05m);
    }

    // ── (b) SIRA KURALI motorda: GrossUp satırları HEP EN SONDA (DisplayOrder karışık verilse bile) ──

    [Fact]
    public void Commission_line_is_last_and_covers_fixed_costs()
    {
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, Plan(
            Fixed(SideCostKind.Packaging, 60m, order: 0),
            Fixed(SideCostKind.Cargo, 40m, order: 1),
            Commission(20m, order: 2)));

        var visible = lines.Where(l => !l.IsDeleted).OrderBy(l => l.LineOrder).ToList();
        visible[^1].SideCostKind.ShouldBe(SideCostKind.Commission);
        visible[^1].DerivedOperation.ShouldBe(RecipeDerivedOperation.GrossUp);
        visible[^1].DerivedBaseMode.ShouldBe(RecipeDerivedBaseMode.AllAbove);

        // GrossUp'ın tabanı (AppliedBase) sabit giderleri DE içermeli (1000 + 60 + 40 = 1100).
        var result = Compute(lines, userBaseCost: 1000m);
        result.Lines[^1].AppliedBase.ShouldBe(1100m);
    }

    [Fact]
    public void GrossUp_items_are_projected_last_even_with_mixed_display_orders()
    {
        // Komisyon DisplayOrder=0 (en başa yazılmış), sabitler 5 ve 9 — motor kuralı: GrossUp yine EN SONDA;
        // sabitler kendi aralarında DisplayOrder sırasıyla (kargo[5] → paketleme[9]).
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, Plan(
            Commission(20m, order: 0),
            Fixed(SideCostKind.Packaging, 60m, order: 9),
            Fixed(SideCostKind.Cargo, 40m, order: 5)));

        var visible = lines.Where(l => !l.IsDeleted).OrderBy(l => l.LineOrder).ToList();
        visible.Select(l => l.SideCostKind).ShouldBe(new SideCostKind?[]
        {
            null, SideCostKind.Cargo, SideCostKind.Packaging, SideCostKind.Commission,
        });
    }

    [Fact]
    public void Later_additions_are_inserted_before_existing_commission_line()
    {
        // Önce yalnız komisyon tanımlıydı; sonra kanala paketleme eklendi → paketleme komisyonun ÖNÜNE girer.
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, Plan(Commission(20m)));
        SideCostRecipeComposer.EnsureLines(lines, Plan(Fixed(SideCostKind.Packaging, 60m), Commission(20m, order: 1)));

        var visible = lines.Where(l => !l.IsDeleted).OrderBy(l => l.LineOrder).ToList();
        visible.Count.ShouldBe(3);
        visible[1].SideCostKind.ShouldBe(SideCostKind.Packaging);
        visible[2].SideCostKind.ShouldBe(SideCostKind.Commission);
    }

    // ── (c) komisyon oranı çözümü: AutoRate → çözülmüş efektif oran (Value fallback) ────────────────

    [Fact]
    public void Commission_rate_resolution_prefers_category_then_channel_default()
    {
        N11CategoryCommissionImporter.ResolveCommissionRate(8m, 15m).ShouldBe(8m);
        N11CategoryCommissionImporter.ResolveCommissionRate(null, 15m).ShouldBe(15m);
        N11CategoryCommissionImporter.ResolveCommissionRate(null, null).ShouldBeNull();
    }

    [Fact]
    public void Effective_commission_rate_includes_mandatory_n11_service_fees_with_vat()
    {
        // N11'in TÜM kategorilerde zorunlu bedelleri fiyata girer (SSOT ozet.md): %21 kategori komisyonu (KDV dahil)
        // + %1 Pazarlama×1,2 + %0,67 Pazaryeri×1,2 (bedeller "+ KDV" → brüt) = %23,004 toplam yük.
        N11CategoryCommissionImporter.ResolveEffectiveCommissionRate(21m, 1m, 0.67m, channelDefaultRate: null)
            .ShouldBe(23.004m);

        // Kategori oranı yoksa kanal varsayılanı taban alınır; bedeller (kategoriden geldiyse) yine eklenir.
        N11CategoryCommissionImporter.ResolveEffectiveCommissionRate(null, 1m, 0.67m, 15m).ShouldBe(17.004m);

        // Bedel verisi import edilmemişse (null) yalnız komisyon — eski davranışla birebir.
        N11CategoryCommissionImporter.ResolveEffectiveCommissionRate(21m, null, null, 15m).ShouldBe(21m);

        // Hiçbir bileşen yoksa null (komisyon satırı üretilmez).
        N11CategoryCommissionImporter.ResolveEffectiveCommissionRate(null, null, null, null).ShouldBeNull();
    }

    [Fact]
    public void AutoRate_commission_uses_resolved_rate_and_falls_back_to_item_value()
    {
        // Çözülmüş oran varken AutoRate satırın operand'ı ODUR (Value=15 fallback'te kalır).
        var autoItem = Commission(15m, autoRate: true);
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(
            lines, new SideCostPlan(new[] { autoItem }, ResolvedCommissionRate: 23.004m, VariantOptInEnabled: false));
        lines.Single(l => l.SideCostKind == SideCostKind.Commission).DerivedOperand.ShouldBe(23.004m);

        // Çözülmüş oran yoksa Value fallback.
        var fallbackLines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(
            fallbackLines, new SideCostPlan(new[] { autoItem }, ResolvedCommissionRate: null, VariantOptInEnabled: false));
        fallbackLines.Single(l => l.SideCostKind == SideCostKind.Commission).DerivedOperand.ShouldBe(15m);
    }

    [Fact]
    public void Null_settings_still_produce_commission_from_resolved_rate()
    {
        // Hiç yapılandırılmamış kanal (settings null): N11 kategori komisyonu pazaryeri gerçeğidir — örtük
        // AutoRate kalemiyle yine reçeteye girer (eski davranış korunur).
        //
        // 2026-07-28'den itibaren bu, kanal "Giderler" formu kaldırıldığı için ANA fiyatlama garantisidir:
        // kanallar artık ayarsız (null) doğuyor ve komisyon YALNIZ bu yoldan fiyata giriyor. Test kırmızıya
        // dönerse ürünler komisyon kadar (~%23) UCUZ fiyatlanıyor demektir — sessiz para kaybı.
        var plan = SideCostPlan.From(settings: null, resolvedCommissionRate: 21m, variantOptInEnabled: false);
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan).ShouldBeTrue();
        lines.Single(l => l.SideCostKind == SideCostKind.Commission).DerivedOperand.ShouldBe(21m);
    }

    [Fact]
    public void Empty_settings_produce_no_commission_and_must_never_be_persisted()
    {
        // YASAK DURUM — belgeleme amaçlı test. Ayar nesnesi VAR ama listesi BOŞ ise ("{\"Items\":[]}")
        // kullanıcı "komisyon satırını sildim" demiş sayılır ve komisyon üretilmez. Kanal kaydına bu değerin
        // YAZILMASI, komisyonu sessizce öldürür: ne hata fırlar ne log düşer, yalnız fiyatlar ~%23 düşer.
        //
        // Bu yüzden kanal kaydetme yolunda SideCosts'a boş nesne YAZILMAMALI; ayar yoksa null KALMALI
        // (yukarıdaki testin koruduğu davranış). Buradaki assert, davranışın kendisini değil, "neden boş
        // yazmıyoruz" gerekçesini sabitler.
        var plan = SideCostPlan.From(
            new SideCostSettings(new List<SideCostItem>()),
            resolvedCommissionRate: 21m,
            variantOptInEnabled: false);

        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };

        SideCostRecipeComposer.EnsureLines(lines, plan).ShouldBeFalse();
        lines.ShouldNotContain(l => l.SideCostKind == SideCostKind.Commission);
    }

    [Fact]
    public void No_commission_line_is_generated_without_a_rate()
    {
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, Plan(Fixed(SideCostKind.Packaging, 60m), Commission(0m, order: 1)));

        lines.ShouldNotContain(l => l.SideCostKind == SideCostKind.Commission);
    }

    [Fact]
    public void Disabled_items_do_not_produce_lines_but_keep_their_data()
    {
        // Kapalı kalem (IsEnabled=false) satır üretmez — grid'de durur, veri kaybolmaz (Offsite Ads deseni).
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, Plan(
            Fixed(SideCostKind.Packaging, 60m),
            Commission(3m, order: 1, enabled: false, name: "Offsite Ads")));

        lines.ShouldContain(l => l.SideCostKind == SideCostKind.Packaging);
        lines.ShouldNotContain(l => l.SideCostKind == SideCostKind.Commission);
    }

    // ── (e) idempotency: ikinci uygulamada dublike yok; kullanıcı düzeltmesine dokunulmaz ───────────

    [Fact]
    public void EnsureLines_is_idempotent_and_respects_user_corrections()
    {
        var plan = Plan(
            Fixed(SideCostKind.Packaging, 60m, order: 0),
            Fixed(SideCostKind.Cargo, 40m, order: 1),
            Commission(20m, order: 2));
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan).ShouldBeTrue();
        var countAfterFirst = lines.Count;

        // Kullanıcı komisyon oranını düzeltti (ör. ürün-bazında farklı oran) — composer DOKUNMAZ.
        var commission = lines.Single(l => l.SideCostKind == SideCostKind.Commission);
        commission.DerivedOperand = 12m;

        SideCostRecipeComposer.EnsureLines(lines, plan).ShouldBeFalse();
        lines.Count.ShouldBe(countAfterFirst);
        lines.Single(l => l.SideCostKind == SideCostKind.Commission).DerivedOperand.ShouldBe(12m);
    }

    [Fact]
    public void ReapplyLines_refreshes_marked_lines_but_keeps_user_lines()
    {
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, Plan(Fixed(SideCostKind.Packaging, 60m), Commission(20m, order: 1)));

        // Persist edilmiş gibi davran (Id dolu) → reapply'da IsDeleted işaretlenip yenisi eklenmeli.
        foreach (var line in lines.Where(l => l.SideCostKind is not null))
        {
            line.Id = Guid.NewGuid();
        }

        // Kanal ayarı değişti: paketleme 75, komisyon %25.
        SideCostRecipeComposer.ReapplyLines(lines, Plan(Fixed(SideCostKind.Packaging, 75m), Commission(25m, order: 1))).ShouldBeTrue();

        var visible = lines.Where(l => !l.IsDeleted).OrderBy(l => l.LineOrder).ToList();
        visible.Count.ShouldBe(3);                                       // taban + paketleme + komisyon
        visible[0].SideCostKind.ShouldBeNull();                          // kullanıcı satırı korunur
        visible[1].SideCostKind.ShouldBe(SideCostKind.Packaging);
        visible[1].DerivedOperand.ShouldBe(75m);
        visible[2].SideCostKind.ShouldBe(SideCostKind.Commission);
        visible[2].DerivedOperand.ShouldBe(25m);

        // Eski (persist edilmiş) otomatik satırlar save akışının silmesi için IsDeleted işaretlendi.
        lines.Count(l => l.IsDeleted && l.SideCostKind is not null).ShouldBe(2);
    }

    [Fact]
    public void ReapplyLines_restores_a_deleted_automatic_line()
    {
        // Kullanıcı otomatik kargo satırını silmişti — EnsureLines geri GETİRMEZ (silme kalıcı), ReapplyLines getirir.
        var plan = Plan(Fixed(SideCostKind.Cargo, 40m));
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan);
        lines.RemoveAll(l => l.SideCostKind == SideCostKind.Cargo);

        SideCostRecipeComposer.ReapplyLines(lines, plan).ShouldBeTrue();
        lines.ShouldContain(l => !l.IsDeleted && l.SideCostKind == SideCostKind.Cargo);
    }

    // ── varyant opt-in (sigortalı gönderim deseninin genellemesi): varsayılan KAPALI, varyantta açılır ──

    [Fact]
    public void Opt_in_item_requires_variant_level_enable()
    {
        var closedPlan = Plan(Fixed(SideCostKind.InsuredShipping, 25m, optIn: true));
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, closedPlan);
        lines.ShouldNotContain(l => l.SideCostKind == SideCostKind.InsuredShipping);

        var openPlan = closedPlan with { VariantOptInEnabled = true };
        SideCostRecipeComposer.EnsureLines(lines, openPlan);
        var insured = lines.Single(l => l.SideCostKind == SideCostKind.InsuredShipping);
        insured.DerivedOperation.ShouldBe(RecipeDerivedOperation.Add);
        insured.DerivedOperand.ShouldBe(25m);
    }

    [Fact]
    public void Opt_in_generalization_applies_to_any_kind()
    {
        // RequiresVariantOptIn herhangi bir kalemde işaretlenebilir (Loomis deseninin genellemesi) —
        // ör. opt-in KARGO kalemi de anahtar kapalıyken üretilmez, açılınca üretilir.
        var plan = Plan(Fixed(SideCostKind.Cargo, 40m, optIn: true));
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan);
        lines.ShouldNotContain(l => l.SideCostKind == SideCostKind.Cargo);

        SideCostRecipeComposer.SyncVariantOptInLines(lines, plan with { VariantOptInEnabled = true }).ShouldBeTrue();
        lines.ShouldContain(l => !l.IsDeleted && l.SideCostKind == SideCostKind.Cargo);
    }

    [Fact]
    public void Opt_in_toggle_sync_adds_line_before_commission_without_touching_other_kinds()
    {
        // Kayıtlı reçete: taban + kargo + komisyon; kullanıcı kargo otomatik satırını SİLMİŞTİ (silme kararı kalıcı).
        var plan = Plan(
            Fixed(SideCostKind.Cargo, 40m, order: 0),
            Fixed(SideCostKind.InsuredShipping, 25m, order: 1, optIn: true),
            Commission(20m, order: 2));
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan);
        lines.RemoveAll(l => l.SideCostKind == SideCostKind.Cargo);

        // Varyantta anahtar AÇILDI (save yolu) → YALNIZ opt-in satırı eklenir (komisyonun ÖNÜNE — GrossUp en son
        // kalır); kullanıcının sildiği kargo GERİ GELMEZ (ReapplyLines'tan fark).
        SideCostRecipeComposer.SyncVariantOptInLines(lines, plan with { VariantOptInEnabled = true }).ShouldBeTrue();

        var visible = lines.Where(l => !l.IsDeleted).OrderBy(l => l.LineOrder).ToList();
        visible.Select(l => l.SideCostKind).ShouldBe(new SideCostKind?[] { null, SideCostKind.InsuredShipping, SideCostKind.Commission });
        lines.ShouldNotContain(l => l.SideCostKind == SideCostKind.Cargo);
    }

    [Fact]
    public void Opt_in_toggle_sync_removes_line_when_switched_off()
    {
        var plan = new SideCostPlan(
            new[] { Fixed(SideCostKind.InsuredShipping, 25m, optIn: true) },
            ResolvedCommissionRate: null, VariantOptInEnabled: true);
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan);

        // Persist edilmiş gibi (Id dolu) → OFF: IsDeleted işaretlenir (save akışı silsin; taze klon olsaydı listeden atılırdı).
        lines.Single(l => l.SideCostKind == SideCostKind.InsuredShipping).Id = Guid.NewGuid();
        SideCostRecipeComposer.SyncVariantOptInLines(lines, plan with { VariantOptInEnabled = false }).ShouldBeTrue();
        lines.Single(l => l.SideCostKind == SideCostKind.InsuredShipping).IsDeleted.ShouldBeTrue();

        // İkinci çağrı idempotent: görünür opt-in satırı yok + anahtar kapalı → değişiklik yok.
        SideCostRecipeComposer.SyncVariantOptInLines(lines, plan with { VariantOptInEnabled = false }).ShouldBeFalse();
    }

    [Fact]
    public void Opt_in_percent_mode_applies_percent_over_running_total()
    {
        // Loomis primi deseni: PercentOfCost → Percent (devreden toplam üstünden); 1000 tabanın %2'si = 20 → net 1020.
        var plan = new SideCostPlan(
            new[] { Item(SideCostKind.InsuredShipping, SideCostCalcMode.PercentOfCost, 2m, requiresVariantOptIn: true) },
            ResolvedCommissionRate: null, VariantOptInEnabled: true);
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan);

        var insured = lines.Single(l => l.SideCostKind == SideCostKind.InsuredShipping);
        insured.DerivedOperation.ShouldBe(RecipeDerivedOperation.Percent);

        Compute(lines, userBaseCost: 1000m).Net.ShouldBe(1020m);
    }

    // ── (f) Etsy: USD sabit kalemler değerlemeyle TRY'ye çevrilir; çoklu GrossUp TOPLANMIŞ TEK satır ──

    [Fact]
    public void Etsy_usd_fixed_fee_is_rebased_with_valuation()
    {
        // $0.45 satış-başı sabit @ USD (1 USD = 40 TRY) → 18 TRY; komisyon %12.5 GrossUp EN SON.
        var plan = Plan(
            Fixed(SideCostKind.ChannelFixed, 0.45m, order: 0, currencyUnitId: Usd),
            Commission(12.5m, order: 1));
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan);

        var fixedFee = lines.Single(l => l.SideCostKind == SideCostKind.ChannelFixed);
        fixedFee.PayUnitId.ShouldBe(Usd);

        var result = Compute(lines, userBaseCost: 1000m);
        result.AnyMissingRate.ShouldBeFalse();

        // Sabit kalem satırı: 0.45 × 40 = 18.00 TRY; net = (1000 + 18) ÷ (1 − 0.125) = 1163.43 (yuvarlama satırda).
        var fixedIndex = lines.Where(l => !l.IsDeleted).OrderBy(l => l.LineOrder).ToList().FindIndex(l => l.SideCostKind == SideCostKind.ChannelFixed);
        result.Lines[fixedIndex].Cost.ShouldBe(18.00m);
        result.Net.ShouldBe(Math.Round(1018m / 0.875m, 2));
    }

    [Fact]
    public void Multiple_grossup_items_are_summed_into_a_single_line()
    {
        // Tüm GrossUp ücretleri AYNI satış fiyatı P'nin yüzdesidir: satıcı P(1−c−e) alır → doğru fiyat
        // P = taban ÷ (1−(c+e)/100). Ardışık bölme (÷(1−c)÷(1−e)) fiyatı DÜŞÜK bırakır (2026-07-10 düzeltme).
        // Etsy örneği: komisyon %10 + Offsite Ads %15 → TEK GrossUp satırı, operand 25, EN SONDA.
        var plan = Plan(
            Fixed(SideCostKind.Packaging, 60m, order: 0),
            Commission(10m, order: 1),
            Commission(15m, order: 2, name: "Offsite Ads"));
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan);

        var visible = lines.Where(l => !l.IsDeleted).OrderBy(l => l.LineOrder).ToList();
        visible.Select(l => l.SideCostKind).ShouldBe(new SideCostKind?[]
        {
            null, SideCostKind.Packaging, SideCostKind.Commission,
        });
        visible[^1].DerivedOperation.ShouldBe(RecipeDerivedOperation.GrossUp);
        visible[^1].DerivedOperand.ShouldBe(25m);
    }

    [Fact]
    public void Summed_grossup_preserves_full_seller_proceeds()
    {
        // Sayısal kanıt (koordinatör örneği): taban 1000, marj %0, c=%10 + e=%15 → fiyat = 1000 ÷ 0.75 = 1333.33;
        // satıcının eline 1333.33 × 0.75 = 1000 geçer ✓. Ardışık bölme 1000/0.90/0.85 ≈ 1307.19 verirdi →
        // satıcı her satışta ce·P (~%1,4) eksik alırdı.
        var plan = Plan(Commission(10m, order: 0), Commission(15m, order: 1, name: "Offsite Ads"));
        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan);

        var result = Compute(lines, userBaseCost: 1000m);
        result.Net.ShouldBe(Math.Round(1000m / 0.75m, 2));
        Math.Round(result.Net * 0.75m, 0).ShouldBe(1000m);
    }

    [Fact]
    public void Summed_grossup_rate_must_stay_under_the_denominator_limit()
    {
        // Kalemler tek tek geçerli (60 ve 50 < 100) ama AKTİF toplam 110 → payda 1−Σ/100 negatif olurdu;
        // hem VO ctor'u hem composer fail-fast (sessiz eksik-fiyatlama YOK).
        Should.Throw<BusinessException>(() => new SideCostSettings(new[]
        {
            Commission(60m, order: 0),
            Commission(50m, order: 1, name: "Offsite Ads"),
        })).Code.ShouldBe("TradeXpress:SalesChannel:SideCostRateOutOfRange");

        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        Should.Throw<BusinessException>(() =>
                SideCostRecipeComposer.EnsureLines(lines, Plan(Commission(60m, order: 0), Commission(50m, order: 1))))
            .Code.ShouldBe("TradeXpress:SalesChannel:SideCostRateOutOfRange");

        // Kapalı kalem toplama girmez → aynı ikili, ikincisi kapalıyken geçerli.
        Should.NotThrow(() => new SideCostSettings(new[]
        {
            Commission(60m, order: 0),
            Commission(50m, order: 1, enabled: false),
        }));
    }

    // ── domain guard'ları: gider satırı kuralları fail-fast ─────────────────────────────────────────

    [Fact]
    public void Commission_item_must_use_grossup_mode()
    {
        var ex = Should.Throw<BusinessException>(() =>
            Item(SideCostKind.Commission, SideCostCalcMode.FixedAmount, 10m));
        ex.Code.ShouldBe("TradeXpress:SalesChannel:SideCostCommissionRequiresGrossUp");
    }

    [Fact]
    public void AutoRate_is_only_allowed_on_commission_items()
    {
        var ex = Should.Throw<BusinessException>(() =>
            Item(SideCostKind.Cargo, SideCostCalcMode.FixedAmount, 10m, autoRate: true));
        ex.Code.ShouldBe("TradeXpress:SalesChannel:SideCostAutoRateOnlyForCommission");
    }

    [Fact]
    public void At_most_one_active_autorate_item_is_allowed()
    {
        // Çözülmüş efektif oran TEK AutoRate kalemi varsayar (GetAutoCommissionFallbackRate FirstOrDefault;
        // composer oranı HER AutoRate kaleme uygular) — ikinci AKTİF AutoRate kalemi N11 oranını sessizce
        // 2x saydırırdı (1100÷(1−0,46) ≈ %42 fazla fiyat) → VO ctor fail-fast.
        Should.Throw<BusinessException>(() => new SideCostSettings(new[]
        {
            Commission(15m, order: 0, autoRate: true),
            Commission(5m, order: 1, autoRate: true, name: "İkinci Komisyon"),
        })).Code.ShouldBe("TradeXpress:SalesChannel:SideCostSingleAutoRateItem");

        // Kapalı (IsEnabled=false) AutoRate kalemi satır üretmez → sayılmaz.
        Should.NotThrow(() => new SideCostSettings(new[]
        {
            Commission(15m, order: 0, autoRate: true),
            Commission(5m, order: 1, autoRate: true, enabled: false),
        }));
    }

    [Fact]
    public void Opt_in_items_cannot_use_grossup_mode()
    {
        // Birleşik GrossUp satırının türü BİRİNCİL kalemden gelir — farklı türde opt-in GrossUp karışırsa
        // varyant toggle senkronu (tür-bazlı düşür/üret) birleşik satırı ıskalar, bayat operand kalır → yasak.
        Should.Throw<BusinessException>(() =>
                Item(SideCostKind.InsuredShipping, SideCostCalcMode.GrossUpPercent, 5m, requiresVariantOptIn: true))
            .Code.ShouldBe("TradeXpress:SalesChannel:SideCostOptInGrossUpNotSupported");
    }

    [Fact]
    public void Item_value_guards_follow_calc_mode()
    {
        Should.Throw<BusinessException>(() => Item(SideCostKind.Packaging, SideCostCalcMode.FixedAmount, -1m))
            .Code.ShouldBe("TradeXpress:SalesChannel:SideCostAmountNegative");
        Should.Throw<BusinessException>(() => Item(SideCostKind.InsuredShipping, SideCostCalcMode.PercentOfCost, 101m))
            .Code.ShouldBe("TradeXpress:SalesChannel:SideCostRateOutOfRange");
        Should.Throw<BusinessException>(() => Commission(100m))
            .Code.ShouldBe("TradeXpress:SalesChannel:SideCostRateOutOfRange");
    }

    // ── Çözülmüş KARGO maliyeti (ürünün kargo şablonundan) ──────────────────────────────────────────

    /// <summary>Şablon maliyeti, kanalın düz kargo değerini ÖNCELER. Gerekçe "özgül olan geneli yener":
    /// şablon o gönderinin gerçek firmasını/hizmetini bilir, kanal değeri tüm şablonlar için tek ortalamadır.
    /// Bu geçmezse üç farklı şablonu olan satıcının bütün ürünleri aynı kargo rakamıyla fiyatlanır.</summary>
    [Fact]
    public void Resolved_cargo_cost_overrides_the_flat_channel_value()
    {
        var plan = SideCostPlan.From(
            new SideCostSettings(new List<SideCostItem> { Fixed(SideCostKind.Cargo, 30m) }),
            resolvedCommissionRate: null,
            variantOptInEnabled: false,
            resolvedCargoCost: 85m,
            resolvedCargoCurrencyUnitId: Try);

        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan).ShouldBeTrue();

        var cargo = lines.Single(l => l.SideCostKind == SideCostKind.Cargo);
        cargo.DerivedOperand.ShouldBe(85m);
        cargo.PayUnitId.ShouldBe(Try);   // birim de ŞABLONDAN — tutarla aynı kaynaktan gelmeli
    }

    /// <summary>Şablonda tutar YOKSA davranış aynen eskisi gibi: kalemin kendi değeri kullanılır. Bu, özelliğin
    /// mevcut kurulumları bozmadığının güvencesi.</summary>
    [Fact]
    public void Without_a_resolved_cargo_cost_the_item_value_is_used_unchanged()
    {
        var plan = SideCostPlan.From(
            new SideCostSettings(new List<SideCostItem> { Fixed(SideCostKind.Cargo, 30m) }),
            resolvedCommissionRate: null,
            variantOptInEnabled: false);

        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan).ShouldBeTrue();

        lines.Single(l => l.SideCostKind == SideCostKind.Cargo).DerivedOperand.ShouldBe(30m);
    }

    /// <summary>AYARSIZ kanalda (settings null) şablon maliyeti örtük bir Kargo kalemi üretmeli.
    ///
    /// <para><b>Bu testin taşıdığı yük:</b> kanal "Giderler" formu 2026-07-28'de kaldırıldığından N11 kanalları
    /// pratikte HEP ayarsız (null) doğuyor. Örtük kalem üretilmezse şablona girilen kargo maliyeti hiçbir
    /// reçeteye ulaşamaz — özellik sessizce ölü kalır (hata da vermez).</para></summary>
    [Fact]
    public void Null_settings_still_produce_cargo_from_the_resolved_template_cost()
    {
        var plan = SideCostPlan.From(
            settings: null,
            resolvedCommissionRate: 21m,
            variantOptInEnabled: false,
            resolvedCargoCost: 85m,
            resolvedCargoCurrencyUnitId: Try);

        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan).ShouldBeTrue();

        lines.Single(l => l.SideCostKind == SideCostKind.Cargo).DerivedOperand.ShouldBe(85m);

        // SIRA: kargo (Add) komisyondan (GrossUp) ÖNCE — sabit giderler de komisyona tabidir.
        var cargoOrder = lines.FindIndex(l => l.SideCostKind == SideCostKind.Cargo);
        var commissionOrder = lines.FindIndex(l => l.SideCostKind == SideCostKind.Commission);
        cargoOrder.ShouldBeLessThan(commissionOrder);
    }

    /// <summary>Şablon maliyeti yokken örtük plan ESKİSİ GİBİ yalnız komisyon üretmeli — boş/sıfır maliyetten
    /// "bedava kargo" satırı doğmamalı.</summary>
    [Fact]
    public void Null_settings_without_a_template_cost_produce_commission_only()
    {
        var plan = SideCostPlan.From(settings: null, resolvedCommissionRate: 21m, variantOptInEnabled: false);

        var lines = new List<ProductRecipeLineGraphDto> { UserBaseLine() };
        SideCostRecipeComposer.EnsureLines(lines, plan).ShouldBeTrue();

        lines.ShouldNotContain(l => l.SideCostKind == SideCostKind.Cargo);
        lines.ShouldContain(l => l.SideCostKind == SideCostKind.Commission);
    }
}
