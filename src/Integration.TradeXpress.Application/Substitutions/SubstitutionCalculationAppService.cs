using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
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
    private readonly IRepository<SubstitutionGroup, Guid> _groupRepository;
    private readonly IRepository<SubstitutionGroupItem, Guid> _itemRepository;
    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IMetalReportAppService _metalReportAppService;
    private readonly IEffectivePriceAppService _effectivePriceAppService;
    private readonly ProductRecipeCostCalculator _recipeCostCalculator;
    private readonly IDataFilter _dataFilter;

    public SubstitutionCalculationAppService(
        IRepository<SubstitutionGroup, Guid> groupRepository,
        IRepository<SubstitutionGroupItem, Guid> itemRepository,
        IRepository<Metal, Guid> metalRepository,
        IMetalReportAppService metalReportAppService,
        IEffectivePriceAppService effectivePriceAppService,
        ProductRecipeCostCalculator recipeCostCalculator,
        IDataFilter dataFilter)
    {
        _groupRepository          = groupRepository;
        _itemRepository           = itemRepository;
        _metalRepository          = metalRepository;
        _metalReportAppService    = metalReportAppService;
        _effectivePriceAppService = effectivePriceAppService;
        _recipeCostCalculator     = recipeCostCalculator;
        _dataFilter               = dataFilter;
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
        var metals = await LoadOrderedMetalsAsync(group);
        var availableByMetal = await LoadAvailableQuantitiesAsync(input);
        var costs = await ComputeUnitCostsAsync(metals);

        var commodities = metals
            .Select(m => new SubstitutionCommodity(
                m.Id,
                m.Code,
                m.StableQuantity,
                ToAvailableCount(availableByMetal.GetValueOrDefault(m.Id)),
                costs.UnitCostByMetal.GetValueOrDefault(m.Id)))
            .ToList();

        var solved = SubstitutionSolver.Solve(new SubstitutionSolverInput(
            input.TargetQuantity, group.ToleranceType, group.ToleranceValue, commodities));

        return BuildResult(group, input.TargetQuantity, topN, commodities, solved, costs);
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

    /// <summary>Grup satırlarını tüketim önceliği (DisplayOrder) sırasıyla madene çözer. Satırsız grup,
    /// maden-grubu referansı (ilk fazda desteklenmez), bulunamayan maden ve adet-hesapsız/standart-gramajsız
    /// maden fail-fast'tir (konsept: yalnız IsQuantity + StableQuantity&gt;0 madenler muadil olabilir).</summary>
    private async Task<List<Metal>> LoadOrderedMetalsAsync(SubstitutionGroup group)
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

        // Katalog çözümü salt-okuma: host (TenantId=null) maden kayıtları tenant altında da görünmeli
        // (RecipeCostPopulator.LoadRecipeCatalogAsync ile aynı desen).
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var metals = await AsyncExecuter.ToListAsync(
                (await _metalRepository.GetQueryableAsync()).Where(m => metalIds.Contains(m.Id)));
            metalById = metals.ToDictionary(m => m.Id);
        }

        var resolved = new List<Metal>(ordered.Count);
        foreach (var metalId in metalIds)
        {
            if (!metalById.TryGetValue(metalId, out var metal))
            {
                throw new BusinessException("TradeXpress:Substitution:MetalNotFound")
                    .WithData("GroupCode", group.Code);
            }

            if (!metal.IsQuantity || metal.StableQuantity <= 0m)
            {
                throw new BusinessException("TradeXpress:Substitution:MetalNotPieceTracked")
                    .WithData("MetalCode", metal.Code);
            }

            resolved.Add(metal);
        }

        return resolved;
    }

    /// <summary>Maden-başına KULLANILABİLİR adet (= Net − RezerveÇıkış) — stok raporunun mevcut scoping'i
    /// AYNEN kullanılır (working company + opsiyonel şube/kasa); rezervasyon düşümü raporda zaten yapılmıştır.</summary>
    private async Task<Dictionary<Guid, decimal>> LoadAvailableQuantitiesAsync(SubstitutionCalculationInput input)
    {
        var rows = await _metalReportAppService.GetStockAsync(new MetalReportFilterDto
        {
            BranchId = input.BranchId,
            VaultId  = input.VaultId,
        });

        // Maden tek MainUnit'le izlenir (FollowingUnit) → tipik tek satır; savunmalı toplama yine de yapılır.
        return rows
            .Where(r => r.MetalId != null)
            .GroupBy(r => r.MetalId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.AvailableQuantity));
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
    /// EntryLabor @ EntryLaborUnit). FAIL-FAST (2026-07-10 kullanıcı kararı): yerel birim ya da HERHANGİ bir
    /// madenin satış kuru çözülemezse hesap koşmaz — <c>RatesMissing</c> (eksik maden kodları WithData'da).</summary>
    private async Task<SubstitutionCostData> ComputeUnitCostsAsync(IReadOnlyList<Metal> metals)
    {
        var countryUnitId = await _effectivePriceAppService.GetWorkingLocalCurrencyUnitIdAsync();
        if (countryUnitId is not { } targetUnitId)
        {
            // Ülke (rebase hedefi) birimi çözülemedi → hiçbir madenin kuru yok sayılır.
            throw BuildRatesMissingException(metals.Select(m => m.Code));
        }

        var valuation = await _effectivePriceAppService.GetValuationByBaseAsync(targetUnitId);
        if (valuation.Count == 0)
        {
            throw BuildRatesMissingException(metals.Select(m => m.Code));
        }

        var sellByUnit = valuation.ToDictionary(v => v.Id, v => v.Sell);
        var countryCode = valuation[0].BaseCurrencyCode;

        var inputs = metals.Select(BuildPieceCostInput).ToList();
        var computed = _recipeCostCalculator.Compute(inputs, sellByUnit, countryCode);

        var unitCostByMetal = new Dictionary<Guid, decimal>(metals.Count);
        var missingCodes = new List<string>();
        for (var i = 0; i < metals.Count; i++)
        {
            var line = computed.Lines[i];
            if (line.MissingRate || line.Cost is not { } cost)
            {
                missingCodes.Add(metals[i].Code);
                continue;
            }

            unitCostByMetal[metals[i].Id] = cost;
        }

        if (missingCodes.Count > 0)
        {
            throw BuildRatesMissingException(missingCodes);
        }

        return new SubstitutionCostData(unitCostByMetal, countryCode);
    }

    /// <summary>Kur-eksik fail-fast hatası — eksik maden KODLARI mesaja girer (kullanıcı hangi kur
    /// girişlerini tamamlayacağını görür; sessiz 0-maliyet katılımı YOK).</summary>
    private static BusinessException BuildRatesMissingException(IEnumerable<string> metalCodes)
    {
        return new BusinessException("TradeXpress:Substitution:RatesMissing")
            .WithData("metalCodes", string.Join(", ", metalCodes));
    }

    /// <summary>1 adet parçayı reçete calculator satırına çevirir (Normal ödeme = metal + işçilik bacağı).</summary>
    private static RecipeLineCostInput BuildPieceCostInput(Metal metal)
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
            PayFactor: metal.EntryLabor,
            PayUnitId: metal.EntryLaborUnitId,
            LaborByQuantity: metal.LaborType == MetalLaborType.Quantity,
            ManualAmount: null,
            ManualUnitId: null);
    }

    /// <summary>Solver çıktısını kullanıcı tablosu DTO'suna çevirir (tüm denemeler numaralandırma sırasıyla;
    /// Rank ≤ TopN başarılılar varyant adayı işaretli).</summary>
    private static SubstitutionCalculationResultDto BuildResult(
        SubstitutionGroup group,
        decimal targetQuantity,
        int topN,
        IReadOnlyList<SubstitutionCommodity> commodities,
        SubstitutionSolverResult solved,
        SubstitutionCostData costs)
    {
        var commodityById = commodities.ToDictionary(c => c.Id);

        var effectiveTolerance = group.ToleranceType == ToleranceType.PerMille
            ? targetQuantity * group.ToleranceValue / 1000m
            : group.ToleranceValue;

        var result = new SubstitutionCalculationResultDto
        {
            GroupId               = group.Id,
            GroupCode             = group.Code,
            GroupName             = group.Name,
            TargetQuantity        = targetQuantity,
            ToleranceType         = group.ToleranceType,
            ToleranceValue        = group.ToleranceValue,
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
                    var commodity = commodityById[l.CommodityId];
                    return new SubstitutionTrialLineDto
                    {
                        MetalId     = l.CommodityId,
                        MetalCode   = commodity.Code,
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

        foreach (var filtered in solved.FilteredOut)
        {
            result.FilteredOut.Add(new SubstitutionFilteredOutDto
            {
                MetalId   = filtered.CommodityId,
                MetalCode = filtered.Code,
                Reason    = filtered.Reason,
            });
        }

        return result;
    }

    /// <summary>Maliyet çözümleme sonucu — maden-başına parça maliyeti + para birimi (fail-fast sonrası
    /// DAİMA tam: her grup madeninin kuru çözülmüştür).</summary>
    private sealed record SubstitutionCostData(
        Dictionary<Guid, decimal> UnitCostByMetal,
        string CurrencyCode);
}
