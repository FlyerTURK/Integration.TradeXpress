using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Products;
using Volo.Abp;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Kanal gider satırlarından (<see cref="SideCostSettings.Items"/>) varyant reçetesine OTOMATİK yan-maliyet
/// satırları üreten SAF kurucu (DB'siz — test edilir; N11 + Trendyol + Etsy ORTAK). Satırlar
/// <see cref="SideCostKind"/> ile işaretlenir (idempotent reconcile anahtarı). Projeksiyon DÜZDÜR
/// (<see cref="SideCostCalcMode"/> → reçete işlemi):
/// <list type="bullet">
/// <item><c>FixedAmount</c> → Hizmet satırı + <c>Add</c> (mutlak tutar @ kalem birimi — birim boşsa ülke birimi;
/// dolu — ör. Etsy USD — değerlemeyle rebase edilir).</item>
/// <item><c>PercentOfCost</c> → <c>Percent(AllAbove)</c> (devreden maliyet toplamı üstünden — Loomis primi deseni).</item>
/// <item><c>GrossUpPercent</c> → TEK <c>GrossUp(AllAbove)</c> satırı, HEP EN SONDA (önce FixedAmount+Percent
/// satırları DisplayOrder sırasıyla — SIRA KURALI MOTORDA); sabit giderler de komisyona tabidir (kullanıcı
/// kararı; kâr korunumu matematiği). Komisyonda <c>AutoRate</c> açıksa oran çağıranın çözdüğü efektif orandır
/// (N11: kategori + zorunlu bedeller ×1,20 — <c>ResolveEffectiveCommissionRate</c> SSOT), <c>Value</c> fallback.</item>
/// </list>
///
/// <para><b>Çoklu GrossUp = TOPLANMIŞ TEK satır (2026-07-10 düzeltme — ardışık bölme YASAK):</b> tüm GrossUp
/// ücretleri AYNI satış fiyatı P'nin yüzdesidir; satıcının eline P(1−(c+e)/100) geçer → doğru fiyat
/// P = taban ÷ (1−(c+e)/100). Kalem başına ayrı GrossUp satırı (÷(1−c)÷(1−e)) böleni (1−c)(1−e)=1−c−e+ce yapar
/// → fiyat DÜŞÜK kalır, satıcı her satışta ce·P kadar eksik alır (ör. %9,5+%15 → ~%1,4 kayıp). Bu yüzden
/// uygulanabilir GrossUp kalemlerinin oranları TOPLANIR (Σ &lt; 100 guard'ı hem <see cref="SideCostSettings"/>
/// ctor'unda [Value'lar] hem burada [çözülmüş oranlar]) ve TEK satır üretilir; satırın tür/hizmet etiketi
/// birincil kalemden gelir (Commission-kind varsa o, yoksa ilk katkı veren).</para>
///
/// <para><b>Varyant opt-in (Loomis deseninin genellemesi):</b> <see cref="SideCostItem.RequiresVariantOptIn"/>
/// işaretli kalem yalnız varyantta anahtar AÇIKSA (<c>InsuredShippingEnabled</c>) uygulanır
/// (<see cref="SideCostPlan.VariantOptInEnabled"/>).</para>
///
/// <para><b>Kural (kullanıcı düzeltmesi korunur):</b> aynı TÜRDEN satır zaten varsa DOKUNULMAZ; yoksa eklenir
/// (<see cref="EnsureLines"/>). Kullanıcı otomatik satırı silmişse kendiliğinden GERİ GELMEZ — yalnız açıkça
/// "yeniden uygula" (<see cref="ReapplyLines"/>: işaretlileri düşürüp ayarlardan tazeler) geri getirir.</para>
///
/// <para><b>Fiş hizalaması:</b> her satırın Service-etiket alanına (CommodityId) kalemin <c>ServiceId</c>'si konur →
/// reçete satırı ile fiş dünyasının emtiası AYNI katalog kaydına bağlanır. Karşı cari referansı reçete satırına
/// TAŞINMAZ — kanal ayarında yaşar; fişleme anında oradan okunacak (bu dilimde fiş yazılmaz).</para>
///
/// <para><b>Sigorta yüzdesi matematiği (kullanıcı kararı 2026-07-10 — operand'ı "düzeltmeye" KALKMA):</b> Loomis
/// primi kanal payı HARİÇ satış değeri (satıcı değeri = B×(1+m); B = maliyet ara toplamı: ürün+paketleme+kargo,
/// m = marj) üzerinden alınır: prim = s·B(1+m). Percent satırı maliyet katmanında s operandıyla durur →
/// NetCost = B(1+s) → P = B(1+s)(1+m)/(1−c) → P(1−c) = B(1+m) + s·B(1+m) = satıcı değeri + TAM prim ✓.
/// Yani operand s İKEN fiyat kuruşuna doğrudur; s(1+m)'e çevirmek FAZLA fiyatlar. NOT (ileriki fiş entegrasyonu):
/// reçete satırının GÖRÜNEN tutarı s·B'dir (maliyet katmanı); Loomis'e fiilen ödenecek prim s·B(1+m) —
/// sipariş→fiş akışı primi satış anındaki satıcı değeri üzerinden HESAPLAMALI, reçete satır tutarından
/// KOPYALAMAMALI.</para>
/// </summary>
public static class SideCostRecipeComposer
{
    /// <summary>Eksik yan-maliyet satırlarını ekler (idempotent — türü mevcutsa DOKUNMAZ). GrossUp-olmayan
    /// eklemeler mevcut GrossUp gider satırının ÖNÜNE girer (GrossUp en sonda kalsın); GrossUp eklemeleri en
    /// sona. Değişiklik olduysa true. Yalnız reçete KURULURKEN/yenilenirken çağrılır (klon / Muadil /
    /// yeniden-uygula) — kaydedilmiş reçetenin her okunuşunda DEĞİL (silinen otomatik satır geri gelmesin).</summary>
    public static bool EnsureLines(List<ProductRecipeLineGraphDto> lines, SideCostPlan plan)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(plan);

        var existingKinds = lines
            .Where(l => !l.IsDeleted && l.SideCostKind is not null)
            .Select(l => l.SideCostKind!.Value)
            .ToHashSet();

        var additions = BuildDesiredLines(plan)
            .Where(l => !existingKinds.Contains(l.SideCostKind!.Value))
            .ToList();
        if (additions.Count == 0)
        {
            return false;
        }

        InsertRespectingGrossUpOrder(lines, additions);
        RenumberVisibleLines(lines);
        return true;
    }

    /// <summary>"Yeniden uygula": işaretli (otomatik) satırları düşürür — persist edilmişler IsDeleted (save
    /// silsin), taze klonlar listeden çıkar — ve ayarlardan yeniden üretir. Kullanıcı satırlarına (SideCostKind
    /// null) DOKUNMAZ. Kanal ayarı değişince/otomatik satır silinince çağrılır; idempotent.</summary>
    public static bool ReapplyLines(List<ProductRecipeLineGraphDto> lines, SideCostPlan plan)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(plan);

        var changed = DropMarkedLines(lines, kind => true);
        return EnsureLines(lines, plan) || changed;
    }

    /// <summary>Varyantın opt-in ANAHTARI (sigortalı gönderim deseni) değişince YALNIZ opt-in türlerin
    /// satırlarını ayarlarla hizalar: <see cref="SideCostItem.RequiresVariantOptIn"/> işaretli kalemlerin
    /// türündeki mevcut satırlar düşürülür (persist edilmiş → IsDeleted, taze klon → listeden çıkar), plana göre
    /// yeniden üretilir (GrossUp-olmayanlar GrossUp gider satırının önüne — GrossUp EN SON kalır). DİĞER türlere
    /// (kullanıcının sildiği paketleme/kargo/komisyon dahil) DOKUNMAZ — <see cref="ReapplyLines"/>'tan farkı
    /// budur (toggle, topyekûn tazeleme değildir). Save yolunda bayrak DEĞİŞTİYSE çağrılır; değişiklik olduysa true.
    /// NOT: opt-in kalem GrossUp OLAMAZ (<see cref="SideCostItem"/> ctor guard'ı) — birleşik GrossUp satırı
    /// (türü birincil kalemden gelir) bu yüzden toggle kapsamına hiç girmez; tür-bazlı düşür/üret güvenlidir.</summary>
    public static bool SyncVariantOptInLines(List<ProductRecipeLineGraphDto> lines, SideCostPlan plan)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(plan);

        var optInKinds = plan.Items
            .Where(i => i.RequiresVariantOptIn)
            .Select(i => i.Kind)
            .ToHashSet();
        if (optInKinds.Count == 0)
        {
            return false;
        }

        var changed = DropMarkedLines(lines, optInKinds.Contains);

        // Aynı türde opt-in OLMAYAN kalem de olabilir — plana göre üretim tekrarı her ikisini de doğru kurar
        // (anahtar kapalıysa opt-in kalemler BuildDesiredLines filtresinde zaten elenir).
        var additions = BuildDesiredLines(plan)
            .Where(l => optInKinds.Contains(l.SideCostKind!.Value))
            .ToList();
        if (additions.Count > 0)
        {
            InsertRespectingGrossUpOrder(lines, additions);
            changed = true;
        }

        if (changed)
        {
            RenumberVisibleLines(lines);
        }

        return changed;
    }

    // İşaretli (otomatik) satırlardan koşula uyanları düşürür: persist edilmiş → IsDeleted (save silsin),
    // taze klon → listeden çıkar. Değişiklik olduysa true.
    private static bool DropMarkedLines(List<ProductRecipeLineGraphDto> lines, Func<SideCostKind, bool> shouldDrop)
    {
        var changed = false;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (line.IsDeleted || line.SideCostKind is not { } kind || !shouldDrop(kind))
            {
                continue;
            }

            if (line.Id == Guid.Empty)
            {
                lines.RemoveAt(i);   // henüz persist edilmemiş klon → doğrudan at
            }
            else
            {
                line.IsDeleted = true;   // persist edilmiş → save akışı silsin
            }

            changed = true;
        }

        return changed;
    }

    // SIRA KURALI: GrossUp-olmayan eklemeler mevcut İLK GrossUp gider satırının önüne (GrossUp EN SON kalır);
    // GrossUp eklemeleri listenin sonuna.
    private static void InsertRespectingGrossUpOrder(
        List<ProductRecipeLineGraphDto> lines, List<ProductRecipeLineGraphDto> additions)
    {
        var grossUpIndex = lines.FindIndex(l =>
            !l.IsDeleted && l.SideCostKind is not null && l.DerivedOperation == RecipeDerivedOperation.GrossUp);
        foreach (var addition in additions)
        {
            if (addition.DerivedOperation != RecipeDerivedOperation.GrossUp && grossUpIndex >= 0)
            {
                lines.Insert(grossUpIndex, addition);
                grossUpIndex++;
            }
            else
            {
                lines.Add(addition);
            }
        }
    }

    /// <summary>Plandan üretilecek satırların TAM listesi — sıra kuralı MOTORDA: önce FixedAmount+Percent
    /// kalemleri (DisplayOrder sırasıyla), GrossUp kalemleri HEP EN SONDA (DisplayOrder karışık verilse bile).
    /// Kapalı, değeri boş (≤0) ve anahtarı kapalı opt-in kalemler üretilmez.</summary>
    private static List<ProductRecipeLineGraphDto> BuildDesiredLines(SideCostPlan plan)
    {
        var applicable = plan.Items
            .Where(i => i.IsEnabled && (!i.RequiresVariantOptIn || plan.VariantOptInEnabled))
            .ToList();

        var desired = new List<ProductRecipeLineGraphDto>();

        foreach (var item in applicable.Where(i => i.CalcMode != SideCostCalcMode.GrossUpPercent).OrderBy(i => i.DisplayOrder))
        {
            if (item.Value > 0m)
            {
                desired.Add(BuildLine(item, item.Value));
            }
        }

        // Çoklu GrossUp = TOPLANMIŞ TEK satır (ardışık bölme fiyatı düşük bırakır — sınıf yorumu):
        // oranlar toplanır, satır etiketi birincil kalemden (Commission-kind varsa o, yoksa ilk katkı veren).
        var totalGrossUpRate = 0m;
        SideCostItem? primaryGrossUp = null;
        foreach (var item in applicable.Where(i => i.CalcMode == SideCostCalcMode.GrossUpPercent).OrderBy(i => i.DisplayOrder))
        {
            // AutoRate (yalnız Commission): çağıranın çözdüğü efektif oran (N11 kategori + zorunlu bedeller),
            // yoksa Value fallback. AutoRate kapalıysa oran doğrudan Value.
            var rate = item.AutoRate ? plan.ResolvedCommissionRate ?? item.Value : item.Value;
            if (rate <= 0m)
            {
                continue;
            }

            totalGrossUpRate += rate;
            if (primaryGrossUp is null || (item.Kind == SideCostKind.Commission && primaryGrossUp.Kind != SideCostKind.Commission))
            {
                primaryGrossUp = item;
            }
        }

        if (primaryGrossUp is not null && totalGrossUpRate > 0m)
        {
            // Toplam oran GrossUp payda sınırını aşamaz (1−Σ/100 pozitif kalmalı) — kalemler tek tek geçerli
            // olsa da toplam taşarsa fail-fast (sessiz eksik-fiyatlama YOK).
            if (totalGrossUpRate >= ProductRecipeConsts.GrossUpOperandExclusiveMax)
            {
                throw new BusinessException("TradeXpress:SalesChannel:SideCostRateOutOfRange")
                    .WithData("property", nameof(SideCostItem.Value));
            }

            desired.Add(BuildLine(primaryGrossUp, totalGrossUpRate));
        }

        return desired;
    }

    // Kalem → reçete Hizmet satırı (düz projeksiyon). PayUnitId yalnız Add'de anlamlı (mutlak tutarın birimi;
    // null = ülke birimi, dolu = değerlemeyle rebase).
    private static ProductRecipeLineGraphDto BuildLine(SideCostItem item, decimal operand)
    {
        var operation = item.CalcMode switch
        {
            SideCostCalcMode.PercentOfCost => RecipeDerivedOperation.Percent,
            SideCostCalcMode.GrossUpPercent => RecipeDerivedOperation.GrossUp,
            _ => RecipeDerivedOperation.Add,
        };

        return new ProductRecipeLineGraphDto
        {
            ComponentType = RecipeComponentType.Service,
            CommodityId = item.ServiceId,     // Service-etiket = kalemin hizmet kartı (fiş hizalaması)
            DerivedBaseMode = RecipeDerivedBaseMode.AllAbove,
            DerivedOperation = operation,
            DerivedOperand = operand,
            PayUnitId = operation == RecipeDerivedOperation.Add ? item.CurrencyUnitId : null,
            SideCostKind = item.Kind,
        };
    }

    // Görünür satırları 0..n-1 yeniden numaralar (save akışı da aynı normalizasyonu yapar — hizalı).
    private static void RenumberVisibleLines(List<ProductRecipeLineGraphDto> lines)
    {
        var order = 0;
        foreach (var line in lines.Where(l => !l.IsDeleted))
        {
            line.LineOrder = order++;
        }
    }
}

/// <summary>Composer'ın kanal-çözümlü girdisi — kanal gider satırları + çağıranın çözdüğü efektif komisyon oranı
/// (N11: kategori komisyonu + zorunlu bedeller ×1,20 — <c>ResolveEffectiveCommissionRate</c> SSOT; Trendyol/Etsy:
/// null → AutoRate kalemi Value fallback'ine düşer) + varyantın opt-in anahtarı (sigortalı gönderim deseni).</summary>
public sealed record SideCostPlan(
    IReadOnlyList<SideCostItem> Items,
    decimal? ResolvedCommissionRate,
    bool VariantOptInEnabled)
{
    /// <summary>Kanal ayarlarından plan kurar. Ayar HİÇ yapılandırılmamışsa (null) eski davranış korunur:
    /// çözülmüş komisyon oranı yine reçeteye girer (örtük AutoRate komisyon kalemi — N11 kategori komisyonu
    /// pazaryeri gerçeğidir, ayar beklemez). Ayar VARSA satır listesi TEK kaynaktır (kullanıcı komisyon satırını
    /// sildiyse üretilmez).</summary>
    public static SideCostPlan From(SideCostSettings? settings, decimal? resolvedCommissionRate, bool variantOptInEnabled)
    {
        if (settings is not null)
        {
            return new SideCostPlan(settings.Items, resolvedCommissionRate, variantOptInEnabled);
        }

        var implicitItems = resolvedCommissionRate is > 0m
            ? new List<SideCostItem>
            {
                new(
                    SideCostKind.Commission, displayName: null, SideCostCalcMode.GrossUpPercent, value: 0m,
                    currencyUnitId: null, serviceId: null, SideCostPostingMode.CounterpartyAccount,
                    accountId: null, subAccountId: null, autoRate: true, isEnabled: true,
                    displayOrder: 0, requiresVariantOptIn: false),
            }
            : new List<SideCostItem>();

        return new SideCostPlan(implicitItems, resolvedCommissionRate, variantOptInEnabled);
    }
}
