using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.Orchestration;
using Microsoft.Extensions.Localization;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Products;

/// <summary>Validator çıktısı: issue'lar + sıralı kontrol listesi + "doğrulanabilir mi". Sayaçlar snapshot'tan türer
/// ve burada bir kez hesaplanır — builder DTO'ya kopyalar, ikinci kez saymaz.</summary>
public sealed class ProductSaleValidationResult
{
    public List<SaleReadinessIssueDto> Issues { get; } = new();

    public List<SaleReadinessStepDto> Steps { get; } = new();

    public bool CanVerify { get; set; }

    public int ActiveVariantCount { get; set; }

    public int PricedVariantCount { get; set; }

    public int RecipeVariantCount { get; set; }

    public int SellableVariantCount { get; set; }

    public int StaleVerifiedVariantCount { get; set; }

    public int DraftVariantCount { get; set; }

    public int SuspendedVariantCount { get; set; }

    /// <summary>Bu varyantı doğrulamayı DURDURAN issue var mı (<c>Variant:*</c>/<c>Recipe:*</c> Error, TargetId = varyant).</summary>
    public bool HasBlockingVariantIssue(Guid variantId)
    {
        return Issues.Any(i => i.Severity == SaleReadinessSeverity.Error && i.TargetId == variantId
                               && ProductSaleValidator.IsVariantScoped(i.Code));
    }

    /// <summary>Ürün-düzeyi Error var mı (<c>Product:*</c>) — varsa HİÇBİR varyant doğrulanmaz.</summary>
    public bool HasBlockingProductIssue()
    {
        return Issues.Any(i => i.Severity == SaleReadinessSeverity.Error && ProductSaleValidator.IsProductScoped(i.Code));
    }
}

/// <summary>
/// ÜRÜN SATIŞ DOĞRULAYICISI (2026-08-19 satışa hazırlık paneli) — "bu ürün neden satışta değil?" sorusunun KURAL sınıfı.
/// SAF: girdisi <see cref="ProductSaleSnapshot"/>, DB'ye dokunmaz; <see cref="ProductSaleReadinessBuilder"/>
/// snapshot'ı çeker, bu sınıf yargılar. Aynı kural sınıfını iki tüketici okur: satışa hazırlık paneli
/// (<c>GetSaleReadinessAsync</c>) ve insan doğrulama yolu (<see cref="ProductSaleVerifier"/>) — kural iki yerde
/// yaşasaydı panel "hazır" derken
/// doğrulama reddeder ya da tersi olurdu.
///
/// <para><b>Ağırlık ölçeği Hakan kararlarıyla sabit (2026-08-19):</b> KDV eksikliği ASLA Error değildir (en fazla
/// Warning — "KDV'nin sistemimizde çok da önemi yoktur"); katalog emtiası satırında 0 adet + 0 miktar Error'dır
/// (<see cref="RecipeLineQuantityRule"/> — <see cref="RecipeLineQuantityGate"/> ile AYNI kural). Error doğrulamayı durdurur, Warning
/// raporlanır ama durdurmaz, Info yalnız bilgidir.</para>
///
/// <para><b>Kodlar sözleşmedir</b> (<see cref="SaleReadinessIssueDto.Code"/>): testler ve UI ikon eşlemesi kodu okur,
/// metin lokalizedir. Yeni kural = yeni kod + bu sınıfta bir dal + testte bir fact.</para>
///
/// <para><b>Her issue KAPSAM YOLU taşır</b> (<see cref="SaleReadinessScope"/>, 2026-08-19 Hakan kuralı): issue
/// bulunduğu HER seviyede görünsün diye yol hiyerarşiktir (<c>channels/{id}/variants/{id}/recipe</c>) ve yolu
/// SUNUCU verir. UI "hangi sekme hangi kodu gösterir" diye ikinci bir kural sınıfı yazmaz — ön-ek kıyasıyla
/// kendi kapsamındaki issue'ların en yüksek ağırlığını okur. Yol bir issue'nun ZORUNLU alanıdır: yolsuz issue
/// <c>ProductSaleReadinessPanel</c>'in hiçbir sekmesinde görünmez, yani sessizce kaybolur.</para>
/// </summary>
public class ProductSaleValidator : ITransientDependency
{
    // ── Adım anahtarları (SaleReadinessStepDto.Key — sabit sıra) ──────────────────────────────────────
    public const string StepCategory = "Category";
    public const string StepVariants = "Variants";
    public const string StepRecipe = "Recipe";
    public const string StepImages = "Images";
    public const string StepVerification = "Verification";
    public const string StepChannelProducts = "ChannelProducts";
    public const string StepPush = "Push";

    // ── Issue kodları ─────────────────────────────────────────────────────────────────────────────────
    public const string ProductNoCategory = "Product:NoCategory";
    public const string ProductVatMissing = "Product:VatMissing";
    public const string ProductNoImage = "Product:NoImage";
    public const string ProductNoRecipeTemplate = "Product:NoRecipeTemplate";
    public const string ProductCalculatedWithoutTrackedCommodity = "Product:CalculatedWithoutTrackedCommodity";
    public const string ProductPassive = "Product:Passive";
    public const string ProductNoActiveVariant = "Product:NoActiveVariant";
    public const string ProductMixedCurrency = "Product:MixedCurrency";
    public const string VariantNoSalePrice = "Variant:NoSalePrice";
    public const string VariantNoRecipe = "Variant:NoRecipe";
    public const string RecipeZeroQuantity = "Recipe:ZeroQuantity";
    public const string VariantNotVerified = "Variant:NotVerified";
    public const string VariantVerificationStale = "Variant:VerificationStale";
    public const string VariantSuspended = "Variant:Suspended";
    public const string ChannelNone = "Channel:None";
    public const string ChannelNotPushed = "Channel:NotPushed";
    public const string ChannelPending = "Channel:Pending";
    public const string ChannelStale = "Channel:Stale";
    public const string ChannelLastError = "Channel:LastError";
    public const string ChannelPassive = "Channel:Passive";
    public const string ChannelObstacle = "Channel:Obstacle";
    public const string ChannelMissingRequiredFields = "Channel:MissingRequiredFields";
    public const string ChannelVariantWithoutCommodity = "Channel:VariantWithoutCommodity";

    private readonly IStringLocalizer<TradeXpressResource> _localizer;

    public ProductSaleValidator(IStringLocalizer<TradeXpressResource> localizer)
    {
        _localizer = localizer;
    }

    /// <summary><c>Product:*</c> kodlu issue ürün-düzeyidir: Error ise HİÇBİR varyant doğrulanmaz.</summary>
    public static bool IsProductScoped(string code)
    {
        return code.StartsWith("Product:", StringComparison.Ordinal);
    }

    /// <summary><c>Variant:*</c>/<c>Recipe:*</c> kodlu issue varyant-düzeyidir: Error ise yalnız o varyant
    /// doğrulanmaz. Kanal issue'ları (<c>Channel:*</c>) push'u ilgilendirir, doğrulamayı DEĞİL.</summary>
    public static bool IsVariantScoped(string code)
    {
        return code.StartsWith("Variant:", StringComparison.Ordinal)
               || code.StartsWith("Recipe:", StringComparison.Ordinal);
    }

    public virtual ProductSaleValidationResult Validate(ProductSaleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var result = new ProductSaleValidationResult();

        CountVariants(snapshot, result);
        ValidateProduct(snapshot, result);
        ValidateVariants(snapshot, result);
        ValidateChannels(snapshot, result);

        // En ağırı önde — satışa hazırlık paneli listesi "önce ne düzeltilecek" sırasıyla okunsun. Eşitlikte üretim sırası
        // korunur (OrderByDescending kararlıdır): ürün → varyant → kanal.
        var ordered = result.Issues.OrderByDescending(i => i.Severity).ToList();
        result.Issues.Clear();
        result.Issues.AddRange(ordered);

        // Doğrulanabilirlik: ürün-düzeyi Error yok VE Error taşımayan en az bir aktif varyant var.
        // Adımlardan ÖNCE hesaplanır: doğrulama adımının durumu buna bakar.
        result.CanVerify = !result.HasBlockingProductIssue()
                           && snapshot.ActiveVariants.Any(v => !result.HasBlockingVariantIssue(v.VariantId));

        BuildSteps(snapshot, result);

        return result;
    }

    // ── Sayaçlar ──────────────────────────────────────────────────────────────────────────────────────

    private static void CountVariants(ProductSaleSnapshot snapshot, ProductSaleValidationResult result)
    {
        result.ActiveVariantCount = snapshot.ActiveVariants.Count;
        result.PricedVariantCount = snapshot.ActiveVariants.Count(v => v.SalePrice is not null);
        result.RecipeVariantCount = snapshot.ActiveVariants.Count(v => v.RecipeLines.Count > 0);
        result.SellableVariantCount = snapshot.ActiveVariants.Count(v => snapshot.SellableVariantIds.Contains(v.VariantId));
        result.StaleVerifiedVariantCount = snapshot.ActiveVariants.Count(IsStaleVerified(snapshot));
        result.DraftVariantCount = snapshot.ActiveVariants.Count(v => v.SaleStatus == ProductSaleStatus.Draft);
        result.SuspendedVariantCount = snapshot.ActiveVariants.Count(v => v.SaleStatus == ProductSaleStatus.Suspended);
    }

    /// <summary>Ready ama <see cref="VariantSaleReadinessResolver"/>'dan geçmiyor = <c>VerifiedRecipeStamp</c> bayat
    /// (reçete onaydan sonra değişti). Rozet "Hazır" der, guard kapalıdır — satışa hazırlık paneli bu farkı
    /// görünür kılmak için var.</summary>
    private static Func<ProductSaleVariantSnapshot, bool> IsStaleVerified(ProductSaleSnapshot snapshot)
    {
        return v => v.SaleStatus == ProductSaleStatus.Ready && !snapshot.SellableVariantIds.Contains(v.VariantId);
    }

    // ── Ürün kuralları ────────────────────────────────────────────────────────────────────────────────

    private void ValidateProduct(ProductSaleSnapshot snapshot, ProductSaleValidationResult result)
    {
        if (!snapshot.HasCategory)
        {
            Add(result, SaleReadinessSeverity.Error, ProductNoCategory, StepCategory, SaleReadinessFixTarget.GeneralTab,
                SaleReadinessScope.General, _localizer["SaleReadiness:Issue:ProductNoCategory"]);
        }

        // KDV: yalnız BİLGİ (Hakan 2026-08-19: "KDV'nin sistemimizde çok da önemi yoktur" + aynı gün canlı TEST
        // ürünü bulgusu: Warning, Kategori adımına ünlem + "Bulgu 1" düşürüyor ve kullanıcı bunu ayrı bir sorun
        // sanıyordu). Info adım durumunu ve IssueCount sayacını ETKİLEMEZ, doğrulama sonucuna da taşınmaz —
        // kanal ürünü kendi KDV'sini taşıyabildiği (kanal ?? ürün zinciri) sürece ürün-seviyesi eksiklik dikkat
        // hak etmiyor; satışa hazırlık panelinin issue listesinde bilgi satırı olarak kalır.
        if (snapshot.VatRate is null)
        {
            Add(result, SaleReadinessSeverity.Info, ProductVatMissing, StepCategory, SaleReadinessFixTarget.GeneralTab,
                SaleReadinessScope.General, _localizer["SaleReadiness:Issue:ProductVatMissing"]);
        }

        if (!snapshot.IsActive)
        {
            Add(result, SaleReadinessSeverity.Warning, ProductPassive, StepCategory, SaleReadinessFixTarget.GeneralTab,
                SaleReadinessScope.General, _localizer["SaleReadiness:Issue:ProductPassive"]);
        }

        // REÇETE ŞABLONU SEÇİLMEDİ (2026-08-20 Hakan kararı: şablon seçimi "isteğe bağlı değil, zorunlu olsun ama
        // bu zorunluluk veritabanı seviyesinde olmasın" + "her bir üründe şablon seçilmedi uyarısını versin").
        //
        // AĞIRLIK NEDEN Warning, Error DEĞİL: kod "Product:" ile başladığı için Error olsaydı HasBlockingProductIssue
        // üzerinden ürünün TÜM varyantlarının doğrulanmasını (CanVerify) engellerdi — yani zorunluluk fiilen bir
        // kilide dönüşür, Hakan'ın "veritabanı seviyesinde olmasın" talimatının ötesine geçerdi. Üstelik reçetesini
        // elle ya da sınıflandırma sihirbazıyla kuran ürün MEŞRUDUR; onun satışını topyekûn durdurmak için sebep yok.
        // Warning: her üründe görünür (satışa hazırlık paneli issue listesi + genel bant + Reçete adımı "Dikkat"), ama iş akışını
        // kilitlemez.
        //
        // YOL "general": şablon ürün-DÜZEYİ bir seçimdir ve combo'su Genel grubunda durur — bant ürün formunun
        // üstünde çıksın; adım anahtarı ise Reçete'dir çünkü eksikliğin sonucu reçetede görülür.
        if (snapshot.RecipeTemplateId is null)
        {
            Add(result, SaleReadinessSeverity.Warning, ProductNoRecipeTemplate, StepRecipe, SaleReadinessFixTarget.GeneralTab,
                SaleReadinessScope.General, _localizer["SaleReadiness:Issue:ProductNoRecipeTemplate"]);
        }

        if (snapshot.ImageCount == 0)
        {
            Add(result, SaleReadinessSeverity.Warning, ProductNoImage, StepImages, SaleReadinessFixTarget.MediaTab,
                SaleReadinessScope.Media, _localizer["SaleReadiness:Issue:ProductNoImage"]);
        }

        if (snapshot.ActiveVariants.Count == 0)
        {
            // Aktif varyantı olmayan ürünün satacak bir şeyi yoktur; verifier de "doğrulanacak aktif varyant yok" der.
            Add(result, SaleReadinessSeverity.Error, ProductNoActiveVariant, StepVariants, SaleReadinessFixTarget.VariantsTab,
                SaleReadinessScope.Variants, _localizer["SaleReadiness:Issue:ProductNoActiveVariant"]);
        }

        // KARIŞIK PARA BİRİMİ (2026-08-21 parite borcu erimesi): fiyatlı varyantlar birden çok para biriminde ise
        // pazaryeri push'u kesilir — Trendyol HER karışımda (TRY-only), N11 kanal/ürün birimi seçilmemişken
        // (MixedCurrency fail-fast'leri). Panel kanal-birimini görmediği için kural birimlerin KENDİSİNE bakar;
        // N11'de kanal birimi seçiliyse uyarı fazladan kalır — Warning olduğu için kabul edilir bedel (Error
        // olsaydı doğrulamayı kilitleyip push'un zaten kestiği şeyi ikinci kez kilitlerdi). Birim kaynağı push
        // satırıyla AYNI alandır (ProductVariantDetail.SalePriceCurrencyUnitId) — iki taraf farklı sayı göremez.
        // Fiyatsız varyantın birimi sayılmaz: o satırın kendi fail-fast'i var (Variant:NoSalePrice), birimi push'a girmez.
        var priceCurrencyCount = snapshot.ActiveVariants
            .Where(v => v.SalePrice is not null && v.SalePriceCurrencyUnitId is not null)
            .Select(v => v.SalePriceCurrencyUnitId!.Value)
            .Distinct()
            .Count();
        if (priceCurrencyCount > 1)
        {
            // Yol VARYANTLAR: birim varyant detayında düzenlenir — kullanıcı hangi sekmede düzelteceğini görsün.
            Add(result, SaleReadinessSeverity.Warning, ProductMixedCurrency, StepVariants, SaleReadinessFixTarget.VariantsTab,
                SaleReadinessScope.Variants, _localizer["SaleReadiness:Issue:ProductMixedCurrency"]);
        }

        // Calculated = stok reçeteden türer; takip edilen (CommodityStockFamilies.Tracked) bir katalog satırı yoksa
        // stok zinciri veri bulamaz ve adet sessizce 0'a düşer — ürün "hesaplı" görünür ama hiç satılmaz.
        if (snapshot.StockPolicy == ProductStockPolicy.Calculated
            && snapshot.ActiveVariants.Count > 0
            && !snapshot.ActiveVariants.Any(v => v.RecipeLines.Any(IsTrackedCatalogLine)))
        {
            // Yol VARYANTLAR sekmesidir: issue ürün genelinde doğar ama düzeltmesi varyant reçetelerindedir —
            // kullanıcıyı "Genel" sekmesine göndermek onu düzeltemeyeceği bir yere yönlendirmek olurdu.
            Add(result, SaleReadinessSeverity.Error, ProductCalculatedWithoutTrackedCommodity, StepRecipe,
                SaleReadinessFixTarget.VariantsTab, SaleReadinessScope.Variants,
                _localizer["SaleReadiness:Issue:ProductCalculatedWithoutTrackedCommodity"]);
        }
    }

    private static bool IsTrackedCatalogLine(ProductSaleRecipeLineSnapshot line)
    {
        return IsCatalogLine(line) && CommodityStockFamilies.IsTracked(line.CommodityFamily);
    }

    // ── Varyant kuralları (yalnız aktif) ─────────────────────────────────────────────────────────────

    private void ValidateVariants(ProductSaleSnapshot snapshot, ProductSaleValidationResult result)
    {
        foreach (var variant in snapshot.ActiveVariants)
        {
            if (variant.SalePrice is null)
            {
                AddForVariant(result, SaleReadinessSeverity.Error, VariantNoSalePrice, StepVariants, variant,
                    SaleReadinessScope.Variant(variant.VariantId),
                    _localizer["SaleReadiness:Issue:VariantNoSalePrice", variant.Code]);
            }

            if (variant.RecipeLines.Count == 0)
            {
                // Calculated'da reçete stoğun kaynağıdır → Error; Fixed/Unlimited'da maliyet/fiyat bilgisidir → Warning.
                var severity = snapshot.StockPolicy == ProductStockPolicy.Calculated
                    ? SaleReadinessSeverity.Error
                    : SaleReadinessSeverity.Warning;

                AddForVariant(result, severity, VariantNoRecipe, StepRecipe, variant,
                    SaleReadinessScope.VariantRecipe(variant.VariantId),
                    _localizer["SaleReadiness:Issue:VariantNoRecipe", variant.Code]);
            }

            foreach (var line in variant.RecipeLines)
            {
                if (RecipeLineQuantityRule.IsSatisfied(line.ComponentType, line.Quantity, line.Amount))
                {
                    continue;
                }

                var commodityLabel = !string.IsNullOrWhiteSpace(line.Description)
                    ? line.Description
                    : line.CommodityFamily is { } family
                        ? _localizer[$"Enum:ProcessType:{family}"].Value
                        : _localizer["SaleReadiness:Issue:UnknownCommodity"].Value;

                AddForVariant(result, SaleReadinessSeverity.Error, RecipeZeroQuantity, StepRecipe, variant,
                    SaleReadinessScope.VariantRecipe(variant.VariantId),
                    _localizer["SaleReadiness:Issue:RecipeZeroQuantity", variant.Code, line.LineOrder + 1, commodityLabel]);
            }

            switch (variant.SaleStatus)
            {
                // Doğrulama issue'larının YOLU varyant değil DOĞRULAMA kapsamıdır: düzeltme yeri satışa hazırlık
                // panelinin "Doğrula" düğmesidir, varyant satırı değil (TargetId varyant kalır ki hangi kayıt olduğu okunsun).
                case ProductSaleStatus.Draft:
                    AddForVariant(result, SaleReadinessSeverity.Info, VariantNotVerified, StepVerification, variant,
                        SaleReadinessScope.Verification,
                        _localizer["SaleReadiness:Issue:VariantNotVerified", variant.Code], SaleReadinessFixTarget.Verify);
                    break;

                case ProductSaleStatus.Ready when !snapshot.SellableVariantIds.Contains(variant.VariantId):
                    AddForVariant(result, SaleReadinessSeverity.Warning, VariantVerificationStale, StepVerification, variant,
                        SaleReadinessScope.Verification,
                        _localizer["SaleReadiness:Issue:VariantVerificationStale", variant.Code], SaleReadinessFixTarget.Verify);
                    break;

                case ProductSaleStatus.Suspended:
                    AddForVariant(result, SaleReadinessSeverity.Warning, VariantSuspended, StepVerification, variant,
                        SaleReadinessScope.Verification,
                        _localizer["SaleReadiness:Issue:VariantSuspended", variant.Code], SaleReadinessFixTarget.Verify);
                    break;

                case ProductSaleStatus.Closed:
                    // Kullanıcının kendi kapattığı varyant: issue değil karar — verifier ile yeniden açılabilir,
                    // satışa hazırlık paneli yalnız Info olarak "doğrulanmadı" satırıyla gösterir.
                    AddForVariant(result, SaleReadinessSeverity.Info, VariantNotVerified, StepVerification, variant,
                        SaleReadinessScope.Verification,
                        _localizer["SaleReadiness:Issue:VariantNotVerified", variant.Code], SaleReadinessFixTarget.Verify);
                    break;
            }
        }
    }

    // ── Kanal ürünü kuralları ─────────────────────────────────────────────────────────────────────────

    private void ValidateChannels(ProductSaleSnapshot snapshot, ProductSaleValidationResult result)
    {
        // HİÇ KANAL ÜRÜNÜ YOK = ERROR (2026-08-19 Hakan talimatı: "Kanal ürünleri tabında hiç ürün belirtilmemiş
        // ise Kanal ürünleri tabı KIRMIZI yazı ile görüntülenip toolbarın üstünde uyarısı gösterilmeli"). İlk
        // sürümde Info'ydu ve palet Info'yu renklendirmediği için sekme sessiz kalıyordu — kanalsız ürün
        // pazaryerinde HİÇBİR yerde satılamaz, bu bir bilgi değil engeldir.
        //
        // DOĞRULAMAYI ENGELLEMEZ: kod "Channel:*" ile başladığı için ne ürün ne varyant kapsamlıdır
        // (IsProductScoped/IsVariantScoped false) → CanVerify etkilenmez. Kullanıcı ürünü doğrulayıp SONRA
        // kanal ürününü ekleyebilir; sıra dayatılmaz, yalnız eksik görünür.
        if (snapshot.Channels.Count == 0)
        {
            Add(result, SaleReadinessSeverity.Error, ChannelNone, StepChannelProducts, SaleReadinessFixTarget.ChannelsTab,
                SaleReadinessScope.Channels, _localizer["SaleReadiness:Issue:ChannelNone"]);
            return;
        }

        foreach (var channel in snapshot.Channels)
        {
            var channelScope = SaleReadinessScope.Channel(channel.ChannelProductId);

            if (channel.MissingRequiredFields)
            {
                AddForChannel(result, SaleReadinessSeverity.Error, ChannelMissingRequiredFields, StepChannelProducts, channel,
                    channelScope, _localizer["SaleReadiness:Issue:ChannelMissingRequiredFields", channel.ChannelLabel]);
            }

            if (!channel.IsActive)
            {
                AddForChannel(result, SaleReadinessSeverity.Warning, ChannelPassive, StepChannelProducts, channel,
                    channelScope, _localizer["SaleReadiness:Issue:ChannelPassive", channel.ChannelLabel]);
            }

            if (channel.IsPending)
            {
                AddForChannel(result, SaleReadinessSeverity.Info, ChannelPending, StepPush, channel,
                    channelScope, _localizer["SaleReadiness:Issue:ChannelPending", channel.ChannelLabel]);
            }
            else if (!channel.IsListed)
            {
                AddForChannel(result, SaleReadinessSeverity.Info, ChannelNotPushed, StepPush, channel,
                    channelScope, _localizer["SaleReadiness:Issue:ChannelNotPushed", channel.ChannelLabel]);
            }

            if (channel.IsStale)
            {
                AddForChannel(result, SaleReadinessSeverity.Warning, ChannelStale, StepPush, channel,
                    channelScope, _localizer["SaleReadiness:Issue:ChannelStale", channel.ChannelLabel]);
            }

            if (!string.IsNullOrWhiteSpace(channel.LastError))
            {
                // Mesaj = kanalın kendi cümlesi; çevrilmez, uydurulmaz.
                AddForChannel(result, SaleReadinessSeverity.Warning, ChannelLastError, StepPush, channel,
                    channelScope, _localizer["SaleReadiness:Issue:ChannelLastError", channel.ChannelLabel, channel.LastError]);
            }

            if (!string.IsNullOrWhiteSpace(channel.Obstacle))
            {
                AddForChannel(result, SaleReadinessSeverity.Warning, ChannelObstacle, StepPush, channel,
                    channelScope, _localizer["SaleReadiness:Issue:ChannelObstacle", channel.ChannelLabel, channel.Obstacle]);
            }

            ValidateChannelVariantCommodities(snapshot, result, channel);
        }
    }

    /// <summary>
    /// DERİN ISSUE (2026-08-19 Hakan senaryosu): <i>"kanal ürünü var ama varyantlara temel emtia eklenmemiş"</i>.
    /// Kanal ürünü açılmış olması o ürünün satılmaya ADAY olduğunu söyler; katalog emtiası satırı olmayan bir
    /// varyant ise kanala gitse bile ne stok ne maliyet üretebilir. Issue KANAL ürünü hedefli üretilir (düzeltme
    /// kanal ürünü formunun varyant/reçete bölümünde yapılır) ama yolu varyant reçetesine kadar iner — böylece
    /// aynı tek issue kanal sekmesini, kanal satırını, varyant satırını ve reçete bölümünü birlikte işaretler.
    ///
    /// <para><b>Ağırlık, core taraftaki <c>Variant:NoRecipe</c> ile aynı felsefededir</b>: Calculated'da reçete stoğun
    /// KAYNAĞIdır → Error; Fixed'de maliyet bilgisidir → Warning. İki kural ayrı yaşar çünkü biri ürün formunu,
    /// diğeri kanal sekmesini boyar; ölçüt de farklıdır (satır SAYISI değil, KATALOG satırı varlığı: yalnız
    /// hizmet satırı taşıyan varyant "reçetesi var" görünür ama emtiası yoktur).</para>
    ///
    /// <para><b><c>Unlimited</c> KAPSAM DIŞIDIR</b> (2026-08-19 denetim düzeltmesi): sınıflandırma sihirbazı
    /// yalnız hizmet satırı taşıyan ürünü BİLEREK <c>Unlimited</c> yapar (<see cref="ProductCommodityProvisioner"/>
    /// — "Calculated yapmak stok zincirinin veri bulamayacağı bir hesap açardı"). Böyle bir üründe temel emtia
    /// beklentisi KONUSUZDUR: stok reçeteden türemiyor, kanala daima "stokta var" gidiyor. Issue üretilseydi
    /// kullanıcının KAPATAMAYACAĞI kalıcı bir uyarı olurdu — "emtia ekle" demek, sihirbazın bilerek açmadığı
    /// katalog kaydını açmak demektir.</para>
    ///
    /// <para>Kanal × varyant başına TEK issue — ek sorgu yok, snapshot zaten iki listeyi de taşıyor.</para>
    /// </summary>
    private void ValidateChannelVariantCommodities(
        ProductSaleSnapshot snapshot,
        ProductSaleValidationResult result,
        ProductSaleChannelSnapshot channel)
    {
        if (snapshot.StockPolicy == ProductStockPolicy.Unlimited)
        {
            return;
        }

        // Bilinmeyen/yeni bir politika sessizce muaf tutulmaz: Warning tarafına düşer (görünür ama durdurmaz).
        var severity = snapshot.StockPolicy == ProductStockPolicy.Calculated
            ? SaleReadinessSeverity.Error
            : SaleReadinessSeverity.Warning;

        foreach (var variant in snapshot.ActiveVariants)
        {
            if (variant.RecipeLines.Any(IsCatalogLine))
            {
                continue;
            }

            AddForChannel(result, severity, ChannelVariantWithoutCommodity, StepChannelProducts, channel,
                SaleReadinessScope.ChannelVariantRecipe(channel.ChannelProductId, variant.VariantId),
                _localizer["SaleReadiness:Issue:ChannelVariantWithoutCommodity", channel.ChannelLabel, variant.Code],
                variant.Code);
        }
    }

    /// <summary>"Temel emtia" ölçütü: katalog kaydına bağlı satır. Hizmet satırı (<c>RecipeComponentType.Service</c>)
    /// stoklanan emtia değil ÜCRET kalemidir — sayılsaydı yalnız işçilik taşıyan varyant emtialı görünürdü.</summary>
    private static bool IsCatalogLine(ProductSaleRecipeLineSnapshot line)
    {
        return line.ComponentType == RecipeComponentType.CatalogCommodity;
    }

    // ── Adımlar ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Sabit sıralı kontrol listesi. Durum kuralı her adımda AYNI: Error → Blocked; "yapılmamış" → NotStarted;
    /// Warning → Attention; aksi hâlde Done. "Yapılmamış" ölçütü adıma özgüdür ve aşağıda tek tek yazılıdır.</summary>
    private void BuildSteps(ProductSaleSnapshot snapshot, ProductSaleValidationResult result)
    {
        var anyListed = snapshot.Channels.Any(c => c.IsListed);

        result.Steps.Add(Step(result, StepCategory, SaleReadinessFixTarget.GeneralTab,
            notStarted: false,
            title: _localizer["SaleReadiness:Step:Category"],
            detail: snapshot.HasCategory
                ? _localizer["SaleReadiness:Step:Category:Done"]
                : _localizer["SaleReadiness:Step:Category:Missing"]));

        result.Steps.Add(Step(result, StepVariants, SaleReadinessFixTarget.VariantsTab,
            notStarted: result.ActiveVariantCount == 0,
            title: _localizer["SaleReadiness:Step:Variants"],
            detail: _localizer["SaleReadiness:Step:Variants:Detail", result.ActiveVariantCount, result.PricedVariantCount]));

        result.Steps.Add(Step(result, StepRecipe, SaleReadinessFixTarget.VariantsTab,
            notStarted: result.RecipeVariantCount == 0,
            title: _localizer["SaleReadiness:Step:Recipe"],
            detail: _localizer["SaleReadiness:Step:Recipe:Detail", result.RecipeVariantCount, result.ActiveVariantCount]));

        result.Steps.Add(Step(result, StepImages, SaleReadinessFixTarget.MediaTab,
            notStarted: snapshot.ImageCount == 0,
            title: _localizer["SaleReadiness:Step:Images"],
            detail: _localizer["SaleReadiness:Step:Images:Detail", snapshot.ImageCount]));

        var verification = Step(result, StepVerification, SaleReadinessFixTarget.Verify,
            notStarted: result.SellableVariantCount == 0 && result.StaleVerifiedVariantCount == 0 && result.SuspendedVariantCount == 0,
            title: _localizer["SaleReadiness:Step:Verification"],
            detail: _localizer["SaleReadiness:Step:Verification:Detail", result.SellableVariantCount, result.ActiveVariantCount]);

        // Doğrulanabilir değilse (ürün/varyant Error'ı) doğrulama adımı ENGELLİDİR — kendi issue'su olmasa bile.
        // Aksi hâlde "NotStarted" görünür ve kullanıcı "Doğrula"ya basıp reddedilmeyi deneyerek öğrenirdi.
        if (!result.CanVerify)
        {
            verification.State = SaleReadinessStepState.Blocked;
        }

        result.Steps.Add(verification);

        result.Steps.Add(Step(result, StepChannelProducts, SaleReadinessFixTarget.ChannelsTab,
            notStarted: snapshot.Channels.Count == 0,
            title: _localizer["SaleReadiness:Step:ChannelProducts"],
            detail: _localizer["SaleReadiness:Step:ChannelProducts:Detail", snapshot.Channels.Count]));

        result.Steps.Add(Step(result, StepPush, SaleReadinessFixTarget.ChannelProductForm,
            notStarted: !anyListed,
            title: _localizer["SaleReadiness:Step:Push"],
            detail: _localizer["SaleReadiness:Step:Push:Detail", snapshot.Channels.Count(c => c.IsListed), snapshot.Channels.Count]));
    }

    private static SaleReadinessStepDto Step(
        ProductSaleValidationResult result,
        string key,
        SaleReadinessFixTarget fixTarget,
        bool notStarted,
        string title,
        string? detail)
    {
        var issues = result.Issues.Where(i => i.StepKey == key).ToList();
        var hasError = issues.Any(i => i.Severity == SaleReadinessSeverity.Error);
        var hasWarning = issues.Any(i => i.Severity == SaleReadinessSeverity.Warning);

        SaleReadinessStepState state;
        if (hasError)
        {
            state = SaleReadinessStepState.Blocked;
        }
        else if (notStarted)
        {
            state = SaleReadinessStepState.NotStarted;
        }
        else if (hasWarning)
        {
            state = SaleReadinessStepState.Attention;
        }
        else
        {
            state = SaleReadinessStepState.Done;
        }

        return new SaleReadinessStepDto
        {
            Key = key,
            State = state,
            Title = title,
            Detail = detail,
            FixTarget = fixTarget,
            IssueCount = issues.Count(i => i.Severity != SaleReadinessSeverity.Info),
        };
    }

    // ── Issue ekleme yardımcıları ─────────────────────────────────────────────────────────────────────

    /// <summary>Ürün-düzeyi issue. <paramref name="path"/> ZORUNLUDUR (varsayılanı yok): yolu unutulan issue
    /// <c>ProductSaleReadinessPanel</c>'in hiçbir sekmesinin kapsamına düşmez ve sessizce kaybolurdu — derleyici
    /// burada zorlasın.</summary>
    private static void Add(
        ProductSaleValidationResult result,
        SaleReadinessSeverity severity,
        string code,
        string stepKey,
        SaleReadinessFixTarget fixTarget,
        string path,
        string message)
    {
        result.Issues.Add(new SaleReadinessIssueDto
        {
            Severity = severity,
            Code = code,
            Message = message,
            StepKey = stepKey,
            Path = path,
            FixTarget = fixTarget,
        });
    }

    private static void AddForVariant(
        ProductSaleValidationResult result,
        SaleReadinessSeverity severity,
        string code,
        string stepKey,
        ProductSaleVariantSnapshot variant,
        string path,
        string message,
        SaleReadinessFixTarget fixTarget = SaleReadinessFixTarget.VariantForm)
    {
        result.Issues.Add(new SaleReadinessIssueDto
        {
            Severity = severity,
            Code = code,
            Message = message,
            StepKey = stepKey,
            Path = path,
            FixTarget = fixTarget,
            TargetId = variant.VariantId,
            TargetLabel = variant.Code,
        });
    }

    /// <summary>Kanal ürünü hedefli issue. <paramref name="targetLabel"/> yalnız derin issue'larda verilir
    /// (orada okunması gereken şey kanalın adı değil HANGİ VARYANT olduğudur); boş bırakılırsa kanal etiketi.</summary>
    private static void AddForChannel(
        ProductSaleValidationResult result,
        SaleReadinessSeverity severity,
        string code,
        string stepKey,
        ProductSaleChannelSnapshot channel,
        string path,
        string message,
        string? targetLabel = null)
    {
        result.Issues.Add(new SaleReadinessIssueDto
        {
            Severity = severity,
            Code = code,
            Message = message,
            StepKey = stepKey,
            Path = path,
            FixTarget = SaleReadinessFixTarget.ChannelProductForm,
            TargetId = channel.ChannelProductId,
            TargetLabel = targetLabel ?? channel.ChannelLabel,
            ChannelType = channel.ChannelType,
        });
    }
}
