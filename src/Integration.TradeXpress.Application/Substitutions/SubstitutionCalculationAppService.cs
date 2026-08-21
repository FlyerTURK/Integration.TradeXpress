using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil hesaplama beslemesi (M3) — saf <see cref="SubstitutionSolver"/>'ı GERÇEK verilerle besler:
/// <list type="number">
///   <item><b>Grup:</b> aktif <see cref="SubstitutionGroup"/> + DisplayOrder sıralı satırları
///   (liste sırası = tüketim önceliği); tolerans DAİMA grup ayarından (override yok).</item>
///   <item><b>Parça gramı:</b> <c>Metal.StableQuantity</c> (adet-hesaplı + standart gramaj zorunlu — değilse fail-fast).</item>
///   <item><b>Stok:</b> <see cref="IMetalReportAppService.GetStockAsync"/> → maden-başına
///   <c>AvailableQuantity</c> (= Net − RezerveÇıkış; rezervasyon solver'a hiç girmez). Kapsam raporla
///   birebir: working company (ICurrentCompany) + opsiyonel şube/kasa filtresi — yeni filtre İCAT EDİLMEZ.</item>
///   <item><b>Maliyet:</b> reçete motoruyla AYNI kaynak/motor — <see cref="IEffectivePriceAppService.GetValuationByBaseAsync"/>
///   (SATIŞ kuru, ülke birimine re-base) + <see cref="ProductRecipeCostCalculator"/> (metal bacağı + işçilik bacağı;
///   parça = 1 adet'lik Normal metal satırı). Yerel birim YA DA grup madenlerinden herhangi birinin satış kuru
///   çözülemezse hesap HİÇ koşmaz: <c>RatesMissing</c> BusinessException (eksik maden kodlarıyla) — 0-maliyetli
///   katılım YOK (2026-07-10 kullanıcı kararı: fail-fast; yanıltıcı maliyet sıralaması üretilmez).</item>
/// </list>
/// </summary>
[Authorize(TradeXpressPermissions.Substitutions.Default)]
public class SubstitutionCalculationAppService : TradeXpressAppService, ISubstitutionCalculationAppService
{
    /// <summary>EntityVariant sahip-tipi guard'ı — işçilik join'i yalnız Metal varyantlarına daralır
    /// (başka entity'nin aynı Id'li varyantına çarpma savunması; RecipeCostPopulator ile aynı desen).</summary>
    private const string MetalEntityName = "Metal";

    private readonly IRepository<SubstitutionGroup, Guid> _groupRepository;
    private readonly IRepository<SubstitutionGroupItem, Guid> _itemRepository;
    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IRepository<EntityVariant, Guid> _entityVariantRepository;
    private readonly IRepository<MetalVariantDetail, Guid> _metalVariantDetailRepository;
    private readonly IMetalReportAppService _metalReportAppService;
    private readonly IEffectivePriceAppService _effectivePriceAppService;
    private readonly ProductRecipeCostCalculator _recipeCostCalculator;
    private readonly IDataFilter _dataFilter;
    private readonly SubstitutionPlanContextLoader _planContextLoader;

    public SubstitutionCalculationAppService(
        IRepository<SubstitutionGroup, Guid> groupRepository,
        IRepository<SubstitutionGroupItem, Guid> itemRepository,
        IRepository<Metal, Guid> metalRepository,
        IRepository<EntityVariant, Guid> entityVariantRepository,
        IRepository<MetalVariantDetail, Guid> metalVariantDetailRepository,
        IMetalReportAppService metalReportAppService,
        IEffectivePriceAppService effectivePriceAppService,
        ProductRecipeCostCalculator recipeCostCalculator,
        IDataFilter dataFilter,
        SubstitutionPlanContextLoader planContextLoader)
    {
        _groupRepository          = groupRepository;
        _itemRepository           = itemRepository;
        _metalRepository          = metalRepository;
        _entityVariantRepository  = entityVariantRepository;
        _metalVariantDetailRepository = metalVariantDetailRepository;
        _metalReportAppService    = metalReportAppService;
        _effectivePriceAppService = effectivePriceAppService;
        _recipeCostCalculator     = recipeCostCalculator;
        _dataFilter               = dataFilter;
        _planContextLoader        = planContextLoader;
    }

    public virtual async Task<SubstitutionCalculationResultDto> CalculateAsync(SubstitutionCalculationInput input)
    {
        Check.NotNull(input, nameof(input));

        // Fail-fast: solver da doğrular ama DB'ye hiç gitmeden aynı hata koduyla erken çık.
        if (input.TargetQuantity <= 0m)
        {
            throw new BusinessException("TradeXpress:Substitution:RequestedAmountInvalid");
        }

        var topN = input.TopN > 0 ? input.TopN : SubstitutionCalculationConsts.DefaultTopN;

        var group = await LoadActiveGroupAsync(input.SubstitutionGroupId);
        var (toleranceType, toleranceValue) = ResolveTolerance(group, input);
        var groupMetals = await LoadOrderedMetalsAsync(group);
        var variantsByMetal = await LoadVariantCatalogAsync(groupMetals.Select(m => m.Metal.Id).ToList());

        // Varyant boyutu (Dilim-2): her maden, etkin varyant kümesine (override ?? IncludedVariantIds ?? {ana})
        // AYRI aday satırları olarak açılır — aynı PieceWeight, farklı işçilik + farklı varyant stoğu.
        // Dilim-3: ürün-düzeyi override kümesi girdiden gelir (boş = grup ayarı).
        var candidates = BuildCandidates(groupMetals, variantsByMetal, input.OverrideVariantIds);
        var availableByCandidate = await LoadAvailableQuantitiesAsync(input, BuildMainVariantIdByMetal(variantsByMetal));
        var costs = await ComputeUnitCostsAsync(candidates);

        var commodities = candidates
            .Select(c => new SubstitutionCommodity(
                c.Metal.Id,
                c.Metal.Code,
                c.Metal.StableQuantity,
                ToAvailableCount(availableByCandidate.GetValueOrDefault((c.Metal.Id, c.VariantId))),
                costs.UnitCostByCandidate.GetValueOrDefault((c.Metal.Id, c.VariantId)),
                c.VariantId,
                c.VariantCode))
            .ToList();

        var solved = SubstitutionSolver.Solve(new SubstitutionSolverInput(
            input.TargetQuantity, toleranceType, toleranceValue, commodities));

        var result = BuildResult(group, input.TargetQuantity, topN, toleranceType, toleranceValue, commodities, solved, costs);

        // Reçete önizlemesi BURADA (BuildResult saf/statik kalsın): DB'ye gidip bağlam yükler.
        await FillRecipeLinesForVariantCandidatesAsync(result);

        return result;
    }

    /// <summary>Etkin tolerans politikası — varsayılan GRUP ayarı (konsept statüko); Dilim-3 ürün Muadil modu
    /// kalıcı konfigürasyonunu opsiyonel override alanlarıyla geçirir. Tür/değer ya İKİSİ de dolu ya da İKİSİ de
    /// boş; değer negatif olamaz (grup SetTolerance kuralıyla hizalı fail-fast).</summary>
    private static (ToleranceType Type, decimal Value) ResolveTolerance(
        SubstitutionGroup group, SubstitutionCalculationInput input)
    {
        if ((input.ToleranceTypeOverride is null) != (input.ToleranceValueOverride is null))
        {
            throw new BusinessException("TradeXpress:Substitution:ToleranceValueInvalid");
        }

        if (input.ToleranceValueOverride is < 0m)
        {
            throw new BusinessException("TradeXpress:Substitution:ToleranceValueInvalid");
        }

        return input.ToleranceTypeOverride is { } type && input.ToleranceValueOverride is { } value
            ? (type, value)
            : (group.ToleranceType, group.ToleranceValue);
    }

    /// <summary>Grubu yükler — yok (ya da başka şirketin: company filtresi görünmez kılar) → NotFound;
    /// pasif → NotActive (fail-fast).</summary>
    private async Task<SubstitutionGroup> LoadActiveGroupAsync(Guid groupId)
    {
        var group = await _groupRepository.FindAsync(groupId);
        if (group == null)
        {
            throw new BusinessException("TradeXpress:Substitution:GroupNotFound");
        }

        if (!group.IsActive)
        {
            throw new BusinessException("TradeXpress:Substitution:GroupNotActive")
                .WithData("GroupCode", group.Code);
        }

        return group;
    }

    /// <summary>Grup satırlarını tüketim önceliği (DisplayOrder) sırasıyla madene çözer (satırın opt-in
    /// varyant kümesiyle birlikte). Satırsız grup, maden-grubu referansı (ilk fazda desteklenmez), bulunamayan
    /// maden ve adet-hesapsız/standart-gramajsız maden fail-fast'tir (konsept: yalnız IsQuantity +
    /// StableQuantity&gt;0 madenler muadil olabilir).</summary>
    private async Task<List<OrderedGroupMetal>> LoadOrderedMetalsAsync(SubstitutionGroup group)
    {
        var items = await _itemRepository.GetListAsync(i => i.SubstitutionGroupId == group.Id);
        if (items.Count == 0)
        {
            throw new BusinessException("TradeXpress:Substitution:GroupHasNoItems")
                .WithData("GroupCode", group.Code);
        }

        var ordered = items.OrderBy(i => i.DisplayOrder).ThenBy(i => i.CreationTime).ToList();
        if (ordered.Any(i => i.MetalId == null))
        {
            throw new BusinessException("TradeXpress:Substitution:MetalGroupItemNotSupported")
                .WithData("GroupCode", group.Code);
        }

        var metalIds = ordered.Select(i => i.MetalId!.Value).ToList();
        Dictionary<Guid, Metal> metalById;

        // Katalog çözümü salt-okuma. Eski gerekçe "host (TenantId=null) maden kayıtları görünsün" idi —
        // görev #4 ile GEÇERSİZ (emtia ICompanyOwned; host'ta üretilemiyor). Filtre kapatma korunur çünkü
        // sorgu metalIds ile daraltılmıştır, AMA tenant bacağı ELLE geri konur: kapatma, başka tenant'ın
        // madeninin id ile çözülmesine açık kapı bırakıyordu (kod-inceleme bulgusu #15).
        var tenantId = CurrentTenant.Id;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var metals = await AsyncExecuter.ToListAsync(
                (await _metalRepository.GetQueryableAsync())
                    .Where(m => metalIds.Contains(m.Id))
                    .Where(m => m.TenantId == null || m.TenantId == tenantId));
            metalById = metals.ToDictionary(m => m.Id);
        }

        var resolved = new List<OrderedGroupMetal>(ordered.Count);
        foreach (var item in ordered)
        {
            if (!metalById.TryGetValue(item.MetalId!.Value, out var metal))
            {
                throw new BusinessException("TradeXpress:Substitution:MetalNotFound")
                    .WithData("GroupCode", group.Code);
            }

            if (!metal.IsQuantity || metal.StableQuantity <= 0m)
            {
                throw new BusinessException("TradeXpress:Substitution:MetalNotPieceTracked")
                    .WithData("MetalCode", metal.Code);
            }

            resolved.Add(new OrderedGroupMetal(metal, item.IncludedVariantIds.ToList()));
        }

        return resolved;
    }

    /// <summary>Grup madenlerinin varyant kataloğu — tek batch. <b>ICompanyScoped kapalı ŞART:</b> varyant satırı
    /// madeninkinden FARKLI bir CompanyId taşıyabildiğinden working-context'te katalog boşalıyordu.
    /// <b>IMultiTenant kapalı</b> ise yalnız sorgu kolaylığı içindir; tenant bacağı ELLE geri konur — aksi halde
    /// başka tenant'ın varyantı id ile çözülebiliyordu (kod-inceleme bulgusu #15). Eski gerekçedeki "host-seviyesi
    /// katalog" görev #4 ile geçersizdir.</summary>
    private async Task<Dictionary<Guid, List<EntityVariant>>> LoadVariantCatalogAsync(List<Guid> metalIds)
    {
        var tenantId = CurrentTenant.Id;

        List<EntityVariant> variants;
        using (_dataFilter.Disable<IMultiTenant>())
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            variants = await AsyncExecuter.ToListAsync(
                (await _entityVariantRepository.GetQueryableAsync())
                    .Where(v => v.EntityName == MetalEntityName && metalIds.Contains(v.EntityId))
                    .Where(v => v.TenantId == null || v.TenantId == tenantId));
        }

        return variants
            .GroupBy(v => v.EntityId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>Aday listesi — her grup madeni, ETKİN varyant kümesine
    /// (<see cref="SubstitutionEffectiveVariantResolver"/>: override ?? IncludedVariantIds ?? {ana varyant})
    /// ayrı aday satırları olarak açılır; aday sırası = tüketim önceliği + küme içi kullanıcı sırası.
    /// <b>Override (Dilim-3):</b> ürünün DÜZ override listesi maden başına katalog varyantlarıyla KESİŞTİRİLİR
    /// (kullanıcı sırası korunur); kesişimi boş kalan maden gruptan devralır (boş=devral semantiği — panelin
    /// "gruptan devralınıyor" durumu).
    /// <para><b>Bayat id budaması (kod-inceleme düzeltmesi):</b> grubun IncludedVariantIds'i de katalogla
    /// KESİŞTİRİLİR — override yoluyla simetrik öz-onarım. Gerekçe: kapsam artık her grup kalemi için somut
    /// id'lerle materyalize ediliyor ve varyantlar rutin olarak soft-delete edilebiliyor (EntityVariantSynchronizer,
    /// bir nitelik değeri kalkınca ilgili varyantı siler) → eski fail-fast, sıradan bir katalog düzenlemesini o
    /// madeni içeren HER grubun hesabını (ürün formu, hesaplama sayfası, kanala-uygula) kilitleyen bir kesintiye
    /// çeviriyordu. Kesişim boşalırsa resolver ana varyanta düşer (statüko).</para></summary>
    private static List<MetalVariantCandidate> BuildCandidates(
        List<OrderedGroupMetal> groupMetals,
        Dictionary<Guid, List<EntityVariant>> variantsByMetal,
        IReadOnlyList<Guid>? overrideVariantIds)
    {
        var candidates = new List<MetalVariantCandidate>();
        foreach (var groupMetal in groupMetals)
        {
            var metal = groupMetal.Metal;
            var metalVariants = variantsByMetal.GetValueOrDefault(metal.Id) ?? new List<EntityVariant>();
            var mainVariantId = metalVariants.FirstOrDefault(v => v.IsMain)?.Id;

            var metalVariantIds = metalVariants.Select(v => v.Id).ToHashSet();
            // NULL KORUNUR — resolver'da "liste yok" (grup modu) ile "liste boş" (ürün bu madeni istemiyor)
            // FARKLI anlamlara geldi. Burada null'ı boş listeye çevirmek ikisini tekrar eşitler ve ürünün
            // kaldırma kararını sessizce yutar.
            var overrideForMetal = overrideVariantIds is null
                ? null
                : overrideVariantIds.Where(metalVariantIds.Contains).ToList();

            // Dahil-varyant listesi de katalogla kesiştirilir (bayat id budaması — override ile simetrik öz-onarım).
            var includedForMetal = groupMetal.IncludedVariantIds
                .Where(metalVariantIds.Contains)
                .ToList();

            var effective = SubstitutionEffectiveVariantResolver.Resolve(
                overrideForMetal, includedForMetal, mainVariantId);

            foreach (var variantId in effective)
            {
                if (variantId is { } id)
                {
                    var variant = metalVariants.FirstOrDefault(v => v.Id == id);
                    if (variant == null)
                    {
                        throw new BusinessException("TradeXpress:Substitution:IncludedVariantNotFound")
                            .WithData("MetalCode", metal.Code);
                    }

                    candidates.Add(new MetalVariantCandidate(metal, id, variant.Code));
                }
                else
                {
                    // Katalog varyantı olmayan maden (legacy) — tek varyantsız aday (statüko).
                    candidates.Add(new MetalVariantCandidate(metal, null, null));
                }
            }
        }

        return candidates;
    }

    /// <summary>Maden → ana varyant id eşlemesi (stok satırı normalizasyonu için).</summary>
    private static Dictionary<Guid, Guid> BuildMainVariantIdByMetal(Dictionary<Guid, List<EntityVariant>> variantsByMetal)
    {
        var result = new Dictionary<Guid, Guid>(variantsByMetal.Count);
        foreach (var (metalId, variants) in variantsByMetal)
        {
            if (variants.FirstOrDefault(v => v.IsMain) is { } main)
            {
                result[metalId] = main.Id;
            }
        }

        return result;
    }

    /// <summary>(Maden, varyant)-başına KULLANILABİLİR adet (= Net − RezerveÇıkış) — stok raporunun mevcut
    /// scoping'i AYNEN kullanılır (working company + opsiyonel şube/kasa); rezervasyon düşümü raporda zaten
    /// yapılmıştır. <b>Normalizasyon (kesin karar, Dilim-2):</b> satırda VariantId null ise ANA varyanta
    /// normalize edilir — tek-varyantlı/legacy hareketler ana havuza akar; ana varyantı olmayan maden
    /// null anahtarda kalır (varyantsız legacy aday onu tüketir).</summary>
    private async Task<Dictionary<(Guid MetalId, Guid? VariantId), decimal>> LoadAvailableQuantitiesAsync(
        SubstitutionCalculationInput input,
        Dictionary<Guid, Guid> mainVariantIdByMetal)
    {
        var rows = await _metalReportAppService.GetStockAsync(new MetalReportFilterDto
        {
            BranchId = input.BranchId,
            VaultId  = input.VaultId,
        });

        // Rapor zaten (maden, varyant, birim) kırılımlıdır; birimler savunmacı toplanır.
        return rows
            .Where(r => r.MetalId != null)
            .GroupBy(r => (MetalId: r.MetalId!.Value, VariantId: NormalizeVariant(r.MetalId!.Value, r.VariantId)))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.AvailableQuantity));

        Guid? NormalizeVariant(Guid metalId, Guid? variantId)
        {
            if (variantId is { } id)
            {
                return id;
            }

            return mainVariantIdByMetal.TryGetValue(metalId, out var mainId) ? mainId : null;
        }
    }

    /// <summary>Kullanılabilir adet → solver girdisi: tam parça sayısı (kesirli adet parça sayılmaz, aşağı yuvarlanır).</summary>
    private static int ToAvailableCount(decimal availableQuantity)
    {
        if (availableQuantity <= 0m)
        {
            return 0;
        }

        return (int)Math.Floor(availableQuantity);
    }

    /// <summary>Parça (1 adet) maliyeti — reçete motoruyla AYNI kaynak ve AYNI motor: ülke birimi +
    /// <c>GetValuationByBaseAsync</c> SATIŞ kuru dict'i + <see cref="ProductRecipeCostCalculator"/>'a
    /// 1 adet'lik Normal metal satırı (metal bacağı: StableQuantity × Factor @ FollowingUnit; işçilik bacağı:
    /// EntryLabor @ EntryLaborUnit). <b>Varyant boyutu (Dilim-2):</b> işçilik ADAYIN SEÇİLİ VARYANTININ
    /// MetalVariantDetail'inden okunur (IsMain daraltması kalktı — sözlük varyant-anahtarlı); işçilik detayı
    /// olmayan aday sessiz-0 + LogWarning yolunda kalır (statüko). FAIL-FAST (2026-07-10 kullanıcı kararı):
    /// yerel birim ya da HERHANGİ bir madenin satış kuru çözülemezse hesap koşmaz — <c>RatesMissing</c>
    /// (eksik maden kodları WithData'da; maden-başına tek kez raporlanır).</summary>
    private async Task<SubstitutionCostData> ComputeUnitCostsAsync(IReadOnlyList<MetalVariantCandidate> candidates)
    {
        var countryUnitId = await _effectivePriceAppService.GetWorkingLocalCurrencyUnitIdAsync();
        if (countryUnitId is not { } targetUnitId)
        {
            // Ülke (rebase hedefi) birimi çözülemedi → hiçbir madenin kuru yok sayılır.
            throw BuildRatesMissingException(DistinctMetalCodes(candidates));
        }

        var valuation = await _effectivePriceAppService.GetValuationByBaseAsync(targetUnitId);
        if (valuation.Count == 0)
        {
            throw BuildRatesMissingException(DistinctMetalCodes(candidates));
        }

        var sellByUnit = valuation.ToDictionary(v => v.Id, v => v.Sell);
        var countryCode = valuation[0].BaseCurrencyCode;

        var variantIds = candidates
            .Where(c => c.VariantId != null)
            .Select(c => c.VariantId!.Value)
            .Distinct()
            .ToList();

        // İşçilik katalog varyantından okunur: EntityVariant + MetalVariantDetail. ICompanyScoped kapatılır
        // çünkü varyant satırı madeninkinden farklı CompanyId taşıyabiliyor → working-context'te işçilik
        // sessizce 0'a düşüyor ve solver sıralaması yanlış kuruluyordu. IMultiTenant de kapatılır ama tenant
        // bacağı ELLE geri konur (bulgu #15): kapatma başka tenant'ın varyantına id ile erişime açıktı.
        // Eski yorumdaki "HOST-seviyesi katalog" gerekçesi görev #4 ile geçersizdir.
        // Sözlük VARYANT-anahtarlı: etkin kümedeki TÜM varyantların işçiliği yüklenir (IsMain filtresi yok).
        var laborTenantId = CurrentTenant.Id;
        Dictionary<Guid, (decimal EntryLabor, Guid? EntryLaborUnitId, MetalLaborType LaborType)> laborByVariant;
        using (_dataFilter.Disable<IMultiTenant>())
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            var variantsQuery = await _entityVariantRepository.GetQueryableAsync();
            var detailsQuery = await _metalVariantDetailRepository.GetQueryableAsync();

            var laborDetails = await AsyncExecuter.ToListAsync(
                from v in variantsQuery
                join d in detailsQuery on v.Id equals d.EntityVariantId
                where v.EntityName == MetalEntityName && variantIds.Contains(v.Id)
                   && (v.TenantId == null || v.TenantId == laborTenantId)
                select new { v.Id, d.EntryLabor, d.EntryLaborUnitId, d.LaborType }
            );
            laborByVariant = laborDetails.ToDictionary(
                x => x.Id,
                x => (x.EntryLabor, x.EntryLaborUnitId, x.LaborType));
        }

        var inputs = new List<RecipeLineCostInput>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (candidate.VariantId is { } variantId && laborByVariant.TryGetValue(variantId, out var labor))
            {
                inputs.Add(BuildPieceCostInput(candidate.Metal, labor.EntryLabor, labor.EntryLaborUnitId, labor.LaborType));
            }
            else
            {
                // Sessiz-0 fallback KORUNUR (ürün kararı: fail-fast'e çevrilmedi) ama görünürlük için uyarılır —
                // işçiliksiz katılım solver sıralamasını sistematik olarak bu adaya doğru eğer (per-varyant uyarı).
                Logger.LogWarning(
                    "Muadil hesap: aday için işçilik detayı bulunamadı, işçilik 0 varsayıldı. MetalId={MetalId}, MetalCode={MetalCode}, VariantCode={VariantCode}",
                    candidate.Metal.Id, candidate.Metal.Code, candidate.VariantCode);
                inputs.Add(BuildPieceCostInput(candidate.Metal, entryLabor: 0m, entryLaborUnitId: null, laborType: MetalLaborType.Amount));
            }
        }

        var computed = _recipeCostCalculator.Compute(inputs, sellByUnit, countryCode);

        var unitCostByCandidate = new Dictionary<(Guid MetalId, Guid? VariantId), decimal>(candidates.Count);
        var missingCodes = new List<string>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var line = computed.Lines[i];
            if (line.MissingRate || line.Cost is not { } cost)
            {
                // Kur maden-başınadır (FollowingUnit) — aynı madenin çok varyantı tek kez raporlanır.
                if (!missingCodes.Contains(candidates[i].Metal.Code))
                {
                    missingCodes.Add(candidates[i].Metal.Code);
                }

                continue;
            }

            unitCostByCandidate[(candidates[i].Metal.Id, candidates[i].VariantId)] = cost;
        }

        if (missingCodes.Count > 0)
        {
            throw BuildRatesMissingException(missingCodes);
        }

        return new SubstitutionCostData(unitCostByCandidate, countryCode);
    }

    /// <summary>Aday listesinden maden kodları — sıra korunur, duplike düşer (RatesMissing raporu).</summary>
    private static IEnumerable<string> DistinctMetalCodes(IReadOnlyList<MetalVariantCandidate> candidates)
    {
        return candidates.Select(c => c.Metal.Code).Distinct();
    }

    /// <summary>Kur-eksik fail-fast hatası — eksik maden KODLARI mesaja girer (kullanıcı hangi kur
    /// girişlerini tamamlayacağını görür; sessiz 0-maliyet katılımı YOK).</summary>
    private static BusinessException BuildRatesMissingException(IEnumerable<string> metalCodes)
    {
        return new BusinessException("TradeXpress:Substitution:RatesMissing")
            .WithData("metalCodes", string.Join(", ", metalCodes));
    }

    /// <summary>1 adet parçayı reçete calculator satırına çevirir (Normal ödeme = metal + işçilik bacağı).</summary>
    private static RecipeLineCostInput BuildPieceCostInput(Metal metal, decimal entryLabor, Guid? entryLaborUnitId, MetalLaborType laborType)
    {
        return new RecipeLineCostInput(
            RecipeComponentType.CatalogCommodity,
            ProcessType.Metal,
            Quantity: 1m,
            Amount: metal.StableQuantity,
            Factor: metal.Factor,
            IsQuantity: true,
            StableQuantity: metal.StableQuantity,
            PriceByQuantity: false,
            EntryPrice: 0m,
            NaturalUnitId: metal.FollowingUnitId,
            PaymentType: ProcessPaymentType.Normal,
            PayFactor: entryLabor,
            PayUnitId: entryLaborUnitId,
            LaborByQuantity: laborType == MetalLaborType.Quantity,
            ManualAmount: null,
            ManualUnitId: null);
    }

    /// <summary>Solver çıktısını kullanıcı tablosu DTO'suna çevirir (tüm denemeler numaralandırma sırasıyla;
    /// Rank ≤ TopN başarılılar varyant adayı işaretli). Tolerans parametreleri ETKİN değerlerdir
    /// (grup ayarı ya da Dilim-3 ürün override'ı) — sonuç tablosu kullanılan politikayı gösterir.</summary>
    private static SubstitutionCalculationResultDto BuildResult(
        SubstitutionGroup group,
        decimal targetQuantity,
        int topN,
        ToleranceType toleranceType,
        decimal toleranceValue,
        IReadOnlyList<SubstitutionCommodity> commodities,
        SubstitutionSolverResult solved,
        SubstitutionCostData costs)
    {
        // Aday anahtarı (MetalId, VariantId) — aynı maden birden çok varyant adayıyla katılabilir (Dilim-2).
        var commodityByKey = commodities.ToDictionary(c => (c.Id, c.VariantId));

        var effectiveTolerance = toleranceType == ToleranceType.PerMille
            ? targetQuantity * toleranceValue / 1000m
            : toleranceValue;

        var result = new SubstitutionCalculationResultDto
        {
            GroupId               = group.Id,
            GroupCode             = group.Code,
            GroupName             = group.Name,
            TargetQuantity        = targetQuantity,
            ToleranceType         = toleranceType,
            ToleranceValue        = toleranceValue,
            EffectiveTolerance    = effectiveTolerance,
            TopN                  = topN,
            TrialCount            = solved.All.Count,
            SuccessCount          = solved.All.Count(t => t.Success),
            TotalAvailableWeight  = solved.TotalAvailableWeight,
            InsufficientStock     = solved.InsufficientStock,
            CostCurrencyCode      = costs.CurrencyCode,
        };

        foreach (var trial in solved.All)
        {
            result.Trials.Add(new SubstitutionTrialDto
            {
                Lines = trial.Lines.Select(l =>
                {
                    var commodity = commodityByKey[(l.CommodityId, l.VariantId)];
                    return new SubstitutionTrialLineDto
                    {
                        MetalId     = l.CommodityId,
                        MetalCode   = commodity.Code,
                        VariantId   = commodity.VariantId,
                        VariantCode = commodity.VariantCode,
                        Count       = l.Count,
                        PieceWeight = commodity.PieceWeight,
                        UnitCost    = commodity.UnitCost,
                    };
                }).ToList(),
                TotalWeight    = trial.Total,
                Deviation      = trial.Total - targetQuantity,
                TotalCost      = trial.TotalCost,
                PieceCount     = trial.PieceCount,
                PackageCount   = trial.PackageCount,
                Success        = trial.Success,
                FailureReason  = trial.FailureReason,
                Rank           = trial.Rank,
                IsTopCandidate = trial.Success && trial.Rank is { } rank && rank <= topN,
            });
        }

        // Deterministik varyant kodu — kayıt anındakiyle AYNI üreticiden (tek kaynak). Tüm denemeler
        // hazır olduktan SONRA üretilir: "varyant adı ayırt ediyor mu" ölçütü denemelerin tamamına bakar.
        var multiVariantMetalIds = SubstitutionCombinationCodeBuilder.MultiVariantMetalIds(result.Trials);
        foreach (var trialDto in result.Trials)
        {
            trialDto.CombinationCode = SubstitutionCombinationCodeBuilder.Build(trialDto, multiVariantMetalIds);
            trialDto.CombinationSummary = SubstitutionCombinationCodeBuilder.BuildSummary(trialDto, multiVariantMetalIds);
        }


        foreach (var filtered in solved.FilteredOut)
        {
            result.FilteredOut.Add(new SubstitutionFilteredOutDto
            {
                MetalId     = filtered.CommodityId,
                MetalCode   = filtered.Code,
                VariantId   = filtered.VariantId,
                VariantCode = filtered.VariantCode,
                Reason      = filtered.Reason,
            });
        }

        return result;
    }

    /// <summary>Maliyet çözümleme sonucu — aday (maden+varyant) başına parça maliyeti + para birimi
    /// (fail-fast sonrası DAİMA tam: her grup madeninin kuru çözülmüştür).</summary>
    private sealed record SubstitutionCostData(
        Dictionary<(Guid MetalId, Guid? VariantId), decimal> UnitCostByCandidate,
        string CurrencyCode);

    /// <summary>Tüketim önceliği sırasındaki grup madeni + satırın opt-in varyant kümesi (boş = yalnız ana).</summary>
    private sealed record OrderedGroupMetal(
        Metal Metal,
        IReadOnlyList<Guid> IncludedVariantIds);

    /// <summary>Solver adayı — maden + etkin kümedeki tek varyant (null = katalog varyantı olmayan legacy maden).</summary>
    private sealed record MetalVariantCandidate(
        Metal Metal,
        Guid? VariantId,
        string? VariantCode);

    /// <summary>
    /// Varyanta dönüşecek kombinasyonların REÇETE satırlarını doldurur — ürün formu kaydetmeden varyantın
    /// içindeki emtiaları gösterebilsin diye (2026-07-27 Hakan isteği: "kaydetmeden otomatik hesaplansın").
    ///
    /// <para><b>Neden hepsi için değil:</b> yüzlerce deneme × satırlar yanıtı gereksiz büyütürdü. Yalnız
    /// varyanta dönüşecek adaylar (<see cref="SubstitutionVariantSelection"/> — kayıt anıyla AYNI kural)
    /// doldurulur; Multi tavanı en geniş durumdur, Single onun alt kümesidir.</para>
    ///
    /// <para>Bağlam yüklenemezse (maden katalogda yok) reçete satırları BOŞ kalır ama hesap DÜŞMEZ: kombinasyon
    /// listesi ve maliyetler zaten hazır — önizlemenin eksikliği yüzünden asıl sonucu kaybetmek yanlış olurdu.</para>
    /// </summary>
    private async Task FillRecipeLinesForVariantCandidatesAsync(SubstitutionCalculationResultDto result)
    {
        var candidates = SubstitutionVariantSelection.Select(result.Trials, SubstitutionVariantMode.Multi);
        if (candidates.Count == 0)
        {
            return;
        }

        var lines = candidates.SelectMany(t => t.Lines).ToList();

        try
        {
            var context = await _planContextLoader.LoadAsync(
                lines.Select(l => l.MetalId).Distinct().ToList(),
                lines.Where(l => l.VariantId != null).Select(l => l.VariantId!.Value).Distinct().ToList());

            foreach (var trial in candidates)
            {
                trial.RecipeLines = SubstitutionPlanContextLoader.BuildRecipeLineDtos(trial, context);
            }
        }
        catch (BusinessException ex) when (ex.Code == "TradeXpress:Substitution:MetalNotFound")
        {
            // Önizleme en-iyi-çaba: satırlar boş kalır, kombinasyon sonucu olduğu gibi döner.
            Logger.LogWarning("Muadil reçete önizlemesi kurulamadı ({Code}) — kombinasyonlar yine döndü.", ex.Code);
        }
    }
}
