using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// MUADİL VARYANT MATERYALİZASYONU (ADR-PRODUCT-ORCHESTRATION; 2026-07-25 Hakan kararları:
/// "Uygula butonu olmasın, otomatik o anda oluşturulsun" + Tek/Çoklu alt-modu).
/// O anki stoğa göre kombinasyonları hesaplar (<see cref="ISubstitutionCalculationAppService"/>) ve:
/// <list type="bullet">
///   <item><b>Single:</b> yalnız Rank 1 kombinasyon ANA varyantın reçetesi olur (statükonun sunucu-taraf,
///   otomatik hâli — eski istemci "Uygula" yolunun yerini alır).</item>
///   <item><b>Multi:</b> Rank sırasıyla en fazla <see cref="ProductConsts.SubstitutionMaterializedVariantMax"/>
///   başarılı kombinasyon AYRI varyant olur: Rank 1 = ana, diğerleri müşteriye seçenek.</item>
/// </list>
/// <para><b>Kimlik kararlılığı:</b> varyant, kombinasyon BİLEŞİMİNDEN türetilen deterministik kodla eşlenir —
/// yeniden üretimde aynı bileşim aynı varyant kaydında kalır (Id sabit → kanal StockItem bağları kopmaz);
/// bileşimi stoktan düşenler silinir, yenileri eklenir (EntityVariantSynchronizer'ın key-diff deseni).</para>
/// <para><b>Synchronizer'a dokunulmaz:</b> materyalize varyantlar nitelik-değer bağı TAŞIMAZ (link-less) →
/// synchronizer'ın 0-nitelik dalı (RemoveLinkedVariantsAsync yalnız BAĞLI varyantları siler) bunları YAŞATIR.</para>
/// <para><b>Kur yoksa (RatesMissing):</b> ürün kaydı DÜŞÜRÜLMEZ — materyalizasyon atlanır ve UYARI loglanır
/// (varyantlar bayat kalır; sonraki stok tetiği/kayıt tazeler). Sessiz yutma değil: yalnız bu kod, logla.</para>
/// </summary>
public class SubstitutionVariantMaterializer : ITransientDependency
{
    private readonly ISubstitutionCalculationAppService _calculation;
    private readonly SubstitutionChannelPlanProvider _planContextProvider;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly EntityVariantManager _variantManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;
    private readonly ILogger<SubstitutionVariantMaterializer> _logger;

    private const string ProductEntityName = "Product";
    private const string SubstitutionErrorPrefix = "TradeXpress:Substitution:";

    public SubstitutionVariantMaterializer(
        ISubstitutionCalculationAppService calculation,
        SubstitutionChannelPlanProvider planContextProvider,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        EntityVariantManager variantManager,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        ILogger<SubstitutionVariantMaterializer> logger)
    {
        _calculation = calculation;
        _planContextProvider = planContextProvider;
        _variantRepository = variantRepository;
        _recipeLineRepository = recipeLineRepository;
        _variantManager = variantManager;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
        _logger = logger;
    }

    /// <summary>Ürünün muadil varyantlarını O ANKİ stoğa göre yeniden üretir. Substitution modunda değilse no-op.
    /// Çağıran doğru company bağlamını kurmakla yükümlü (stok okuması ICurrentCompany ister).</summary>
    public virtual async Task MaterializeAsync(Product product)
    {
        if (product.VariantMode != ProductVariantMode.Substitution
            || product.SubstitutionGroupId is not { } groupId
            || product.SubstitutionTargetQuantity is not { } target)
        {
            return;
        }

        SubstitutionCalculationResultDto result;
        try
        {
            result = await _calculation.CalculateAsync(new SubstitutionCalculationInput
            {
                SubstitutionGroupId    = groupId,
                TargetQuantity         = target,
                ToleranceTypeOverride  = product.SubstitutionToleranceType,
                ToleranceValueOverride = product.SubstitutionToleranceValue,
                OverrideVariantIds     = product.SubstitutionOverrideVariantIds.ToList(),
            });
        }
        catch (BusinessException ex) when (ex.Code?.StartsWith(SubstitutionErrorPrefix, StringComparison.Ordinal) == true)
        {
            // Üretim EN-İYİ-ÇABA: hesap ŞU AN koşamıyor diye (kur eksik / grup kalemi yok / stok yok) ürün
            // KAYDI DÜŞÜRÜLMEZ — konfigürasyonun kendi doğrulaması SetSubstitutionConfig'te zaten yapıldı.
            // Varyantlar bayat kalır; sonraki kayıt/stok tetiği tazeler. Yalnız muadil-hesap kodları yakalanır
            // (genel BusinessException DEĞİL — beklenmeyen hata yükselmeye devam eder; §2 sahte-baypas yasak).
            _logger.LogWarning(
                "Muadil materyalizasyonu atlandı ({Code}): Product={ProductCode}. Varyantlar bayat kaldı; "
                + "sonraki kayıt/stok tetiği tazeler.", ex.Code, product.Code);
            return;
        }

        try
        {
            var selected = SelectTrials(product, result);
            var context = await LoadContextAsync(selected);

            if (product.SubstitutionVariantMode == SubstitutionVariantMode.Multi)
            {
                await MaterializeMultiAsync(product, selected, context);
            }
            else
            {
                await MaterializeSingleAsync(product, selected, context);
            }
        }
        catch (BusinessException ex) when (ex.Code?.StartsWith(SubstitutionErrorPrefix, StringComparison.Ordinal) == true)
        {
            // Guard yalniz CalculateAsync'i sarmiyordu (inceleme bulgusu #12): baglam yukleme / recete kurma da
            // ayni aileden firlatabilir (MetalNotFound) — urun kaydi yine dusurulmez, loglanir.
            _logger.LogWarning(
                "Muadil materyalizasyonu atlandi ({Code}): Product={ProductCode}.", ex.Code, product.Code);
        }
    }

    // ── Seçim: yalnız BAŞARILI adaylar, Rank sırasıyla; Multi'de tavanlı, Single'da yalnız Rank 1. ──
    private static List<SubstitutionTrialDto> SelectTrials(Product product, SubstitutionCalculationResultDto result)
    {
        var successful = result.Trials
            .Where(t => t.Success && t.Rank is not null)
            .OrderBy(t => t.Rank!.Value)
            .ToList();

        return product.SubstitutionVariantMode == SubstitutionVariantMode.Multi
            ? successful.Take(ProductConsts.SubstitutionMaterializedVariantMax).ToList()
            : successful.Take(1).ToList();
    }

    private async Task<SubstitutionChannelPlanContext> LoadContextAsync(List<SubstitutionTrialDto> trials)
    {
        var lines = trials.SelectMany(t => t.Lines).ToList();
        return await _planContextProvider.LoadPlanContextAsync(
            lines.Select(l => l.MetalId).Distinct().ToList(),
            lines.Where(l => l.VariantId != null).Select(l => l.VariantId!.Value).Distinct().ToList());
    }

    // ── SINGLE: tek ana varyant kalır; Rank 1 kombinasyon reçetesi olur (yoksa reçete BOŞALTILMAZ —
    //    stok yokken kayıtlı reçeteyi silmek üretim bilgisini yok ederdi; stok=0 zaten kanala 0 gönderir). ──
    private async Task MaterializeSingleAsync(
        Product product, List<SubstitutionTrialDto> selected, SubstitutionChannelPlanContext context)
    {
        var main = await _variantManager.EnsureMainVariantAsync(ProductEntityName, product.Id, product.CompanyId);

        if (selected.Count == 0)
        {
            main.SetStock(0);
            await _variantRepository.UpdateAsync(main, autoSave: true);
            return;
        }

        var best = selected[0];
        await ReplaceRecipeLinesIfChangedAsync(product.CompanyId, main.Id, best, context);
        main.SetStock(best.PackageCount);
        await _variantRepository.UpdateAsync(main, autoSave: true);

        // Coklu->Tek gecisi (inceleme bulgusu #6): eski materyalize kombinasyon varyantlari Tek modda artik
        // satilmamali — ana disindaki canli varyantlar soft-silinir (Multi'ye donuste ayni kodla dirilirler).
        var others = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == product.Id && v.Id != main.Id));
        foreach (var other in others)
        {
            await _recipeLineRepository.DeleteAsync(l => l.ProductVariantId == other.Id, autoSave: true);
            await _variantRepository.DeleteAsync(other, autoSave: true);
        }
    }

    // ── MULTI: kombinasyon-kod diff'i — ayni bilesim ayni varyantta kalir (Id sabit), dusen pasiflesir/silinir. ──
    private async Task MaterializeMultiAsync(
        Product product, List<SubstitutionTrialDto> selected, SubstitutionChannelPlanContext context)
    {
        // SOFT-DELETE FARKINDALI okuma (inceleme bulgusu #16): (TenantId, EntityName, EntityId, Code) benzersiz
        // indeksi IsDeleted filtresi TASIMAZ — soft-silinmis kod yeniden INSERT edilirse unique ihlali patlar.
        // Ayni kod soft-silinmisse DIRILTILIR (Id korunur, kanal baglari geri gelir); yeni satir acilmaz.
        List<EntityVariant> existing;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            existing = await _asyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.EntityName == ProductEntityName && v.EntityId == product.Id));
        }

        var existingByCode = existing.ToDictionary(v => v.Code, StringComparer.OrdinalIgnoreCase);

        // HIC basarili kombinasyon yoksa (stok tukendi) SILME YOK (inceleme bulgusu #3): kimlikler + receteler
        // KORUNUR, yalniz stoklar 0'a cekilir — stok donunce AYNI varyantlar (ayni Id) yeniden satisa acilir.
        // Eski davranis tum varyantlari silip kanal SKU baglarini kopariyordu.
        if (selected.Count == 0)
        {
            foreach (var variant in existing.Where(v => !v.IsDeleted && v.StockQuantity != 0))
            {
                variant.SetStock(0);
                await _variantRepository.UpdateAsync(variant, autoSave: true);
            }

            await _variantManager.EnsureMainVariantAsync(ProductEntityName, product.Id, product.CompanyId);
            return;
        }

        var targetCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EntityVariant? mainVariant = null;

        foreach (var trial in selected)
        {
            var code = BuildCombinationCode(trial);
            targetCodes.Add(code);

            if (!existingByCode.TryGetValue(code, out var variant))
            {
                variant = new EntityVariant(
                    companyId: product.CompanyId,
                    entityName: ProductEntityName,
                    entityId: product.Id,
                    code: code,
                    name: BuildCombinationName(product.Name, trial),
                    isMain: false,
                    isActive: true);
                await _variantRepository.InsertAsync(variant, autoSave: true);
            }
            else
            {
                if (variant.IsDeleted)
                {
                    variant.IsDeleted = false;   // dirilt: ayni bilesim ayni Id'de yasamaya doner (unique ihlali yok)
                }

                variant.SetName(BuildCombinationName(product.Name, trial));
                variant.SetActive(true);
            }

            variant.SetStock(trial.PackageCount);
            await _variantRepository.UpdateAsync(variant, autoSave: true);
            await ReplaceRecipeLinesIfChangedAsync(product.CompanyId, variant.Id, trial, context);

            mainVariant ??= variant;   // selected Rank sirali geldi -> ilk = Rank 1 = ana aday
        }

        // Bilesimi hedefte olmayan CANLI varyantlar soft-silinir (dirilme yukarida; ANAVARYANT ilk Multi
        // gecisinde mesru olarak duser — yerine Rank 1 ana olur).
        foreach (var stale in existing.Where(v => !v.IsDeleted && !targetCodes.Contains(v.Code)))
        {
            await _recipeLineRepository.DeleteAsync(l => l.ProductVariantId == stale.Id, autoSave: true);
            await _variantRepository.DeleteAsync(stale, autoSave: true);
        }

        if (mainVariant is not null)
        {
            // Rank 1 ana yapilir; manager tekil-ana degismezini korur (digerlerini indirger).
            await _variantManager.SetMainVariantAsync(mainVariant);
        }
    }

    /// <summary>Varyantın reçete satırlarını kombinasyondan YENİDEN kurar (kombinasyon reçetenin SAHİBİDİR —
    /// eski satırlar silinir, taze eklenir). Alan kurulumu kanal köprüsünün kanonik
    /// <c>SubstitutionChannelPlanProvider.BuildRecipeLineDtos</c>'uyla AYNI matematik (hedef DTO değil ENTITY
    /// olduğundan yeniden kullanılamadı; sapma olursa referans orası).</summary>
    private async Task ReplaceRecipeLinesIfChangedAsync(
        Guid companyId, Guid variantId, SubstitutionTrialDto trial, SubstitutionChannelPlanContext context)
    {
        // DEGISMEMIS receteyi silip yeniden yazma (inceleme bulgusu #8): 15-dk repricing turu her muadil urune
        // job bastigindan kosulsuz sil+yaz her turda satir Id'lerini degistirirdi (audit gurultusu + bayat referans).
        var existingLines = await _asyncExecuter.ToListAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(l => l.ProductVariantId == variantId)
                .OrderBy(l => l.LineOrder)
                .Select(l => new { l.CommodityId, l.CommodityVariantId, l.Quantity }));
        var existingSignature = string.Join("|",
            existingLines.Select(l => string.Concat(l.CommodityId, ":", l.CommodityVariantId, ":", l.Quantity)));
        var targetSignature = string.Join("|",
            trial.Lines.Select(l => string.Concat(l.MetalId, ":", l.VariantId, ":", (decimal)l.Count)));
        if (existingSignature == targetSignature)
        {
            return;
        }

        await _recipeLineRepository.DeleteAsync(l => l.ProductVariantId == variantId, autoSave: true);

        for (var i = 0; i < trial.Lines.Count; i++)
        {
            var trialLine = trial.Lines[i];
            if (!context.MetalById.TryGetValue(trialLine.MetalId, out var metal))
            {
                throw new BusinessException("TradeXpress:Substitution:MetalNotFound");
            }

            var labor = trialLine.VariantId is { } variantRef
                ? context.LaborByVariantId.GetValueOrDefault(variantRef)
                : context.MainLaborByMetalId.GetValueOrDefault(trialLine.MetalId);

            var line = new ProductVariantRecipeLine(
                companyId,
                variantId,
                RecipeComponentType.CatalogCommodity,
                lineOrder: i);
            line.SetCatalogCommodity(
                ProcessType.Metal,
                metal.Id,
                trialLine.VariantId,
                quantity: trialLine.Count,
                amount: trialLine.Count * metal.StableQuantity,
                factor: metal.Factor,
                valuationUnitId: metal.FollowingUnitId,
                paymentType: ProcessPaymentType.Normal,
                payFactor: labor?.EntryLabor ?? 0m,
                payUnitId: labor?.EntryLaborUnitId);
            await _recipeLineRepository.InsertAsync(line, autoSave: true);
        }
    }

    // ── Deterministik kimlik: bileşim → kod. "G5.0X1+G1.0X3" (metal kodunun ilk parçası + adet, metal koduna
    //    göre sıralı → aynı bileşim her hesapta AYNI kod). 64'ü aşarsa kararlı kısaltma (hash son eki). ──
    private static string BuildCombinationCode(SubstitutionTrialDto trial)
    {
        var parts = trial.Lines
            .OrderBy(l => l.MetalCode, StringComparer.OrdinalIgnoreCase)
            .Select(l =>
            {
                // TAM metal kodu (bosluksuz) — Split(' ')[0] "G5.0 GR 995" ile "G5.0 GR 9999"u AYNI kimlige
                // indiriyordu (inceleme bulgusu #7: ayni grupta iki ayar -> kombinasyon cakismasi).
                var metalFull = l.MetalCode.Replace(" ", string.Empty);
                var variantPart = string.IsNullOrEmpty(l.VariantCode) ? string.Empty : "." + l.VariantCode;
                return $"{metalFull}{variantPart}X{l.Count}";
            });
        var code = string.Join("+", parts).NormalizeAsCode();

        if (code.Length <= EntityVariantConsts.VariantCodeMaxLength)
        {
            return code;
        }

        // Kararlı kısaltma: içerikten deterministik son ek (GetHashCode DEĞİL — process'e göre değişir).
        var suffix = "~" + StableHash(code);
        return code[..(EntityVariantConsts.VariantCodeMaxLength - suffix.Length)] + suffix;
    }

    private static string BuildCombinationName(string productName, SubstitutionTrialDto trial)
    {
        var composition = string.Join(" + ", trial.Lines
            .OrderBy(l => l.MetalCode, StringComparer.OrdinalIgnoreCase)
            .Select(l => $"{l.MetalCode.Split(' ')[0]}×{l.Count}"));
        var name = $"{productName} — {composition}";
        return name.Length <= EntityVariantConsts.VariantNameMaxLength
            ? name
            : name[..EntityVariantConsts.VariantNameMaxLength];
    }

    /// <summary>Deterministik FNV-1a 32-bit (hex) — string.GetHashCode process-bağımlı olduğundan KULLANILMAZ.</summary>
    private static string StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in value)
            {
                hash = (hash ^ c) * 16777619u;
            }

            return hash.ToString("X8");
        }
    }
}
