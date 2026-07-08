using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Vouchers;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Reçete satır setlerinin CANLI net maliyetini hesaplayan ORTAK motor (SSOT) — ERP ürün reçetesi
/// (<c>ProductAppService</c>) ve N11 kanal-özel reçetesi (<c>SalesChannelTrN11ProductAppService</c>) AYNI motoru
/// paylaşır (DRY). Değerleme dict'i (ülke birimine rebase, SELL bacağı) + katalog canlı verisi TEK çağrıda çekilir,
/// tüm satır setlerinde yeniden kullanılır (perf). Ledger'a YAZMAZ (design-time maliyet).
///
/// <para>Satır DTO'larının (<see cref="ProductRecipeLineGraphDto"/>) satır-başı alanlarını (LineCost/Total/PayTotal/
/// AppliedBase/RunningSubtotal/MainUnitCode/PayUnitCode) yerinde doldurur + set-başı net özetini
/// (<see cref="RecipeSetCost"/>) döner. Türev SelectedLines ordinal'i satırların <c>DerivedSourceKeys</c>'inden
/// çözülür (çağıran bunu GetAsync round-trip'inde önceden doldurur).</para>
/// </summary>
public class RecipeCostPopulator : ITransientDependency
{
    private readonly IEffectivePriceAppService _effectivePriceAppService;
    private readonly ProductRecipeCostCalculator _recipeCostCalculator;
    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IRepository<Jewelry, Guid> _jewelryRepository;
    private readonly IRepository<Stone, Guid> _stoneRepository;
    private readonly IDataFilter _dataFilter;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public RecipeCostPopulator(
        IEffectivePriceAppService effectivePriceAppService,
        ProductRecipeCostCalculator recipeCostCalculator,
        IRepository<Metal, Guid> metalRepository,
        IRepository<Jewelry, Guid> jewelryRepository,
        IRepository<Stone, Guid> stoneRepository,
        IDataFilter dataFilter,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _effectivePriceAppService = effectivePriceAppService;
        _recipeCostCalculator = recipeCostCalculator;
        _metalRepository = metalRepository;
        _jewelryRepository = jewelryRepository;
        _stoneRepository = stoneRepository;
        _dataFilter = dataFilter;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>Her satır setinin (ör. bir varyantın reçetesi) net maliyetini hesaplar — set sırasıyla hizalı sonuç
    /// listesi döner. Satır DTO'larının satır-başı alanları YERİNDE doldurulur. Ülke birimi / değerleme çözülemezse
    /// tüm setler NetCost=null (currency boş) döner. Boş set → NetCost=null + (varsa) ülke birim kodu.</summary>
    public virtual async Task<IReadOnlyList<RecipeSetCost>> PopulateAsync(IReadOnlyList<List<ProductRecipeLineGraphDto>> lineSets)
    {
        var results = new RecipeSetCost[lineSets.Count];
        for (var i = 0; i < results.Length; i++)
        {
            results[i] = new RecipeSetCost(null, string.Empty, false);
        }

        var allLines = lineSets.SelectMany(s => s).ToList();
        if (allLines.Count == 0)
        {
            return results;
        }

        var countryUnitId = await _effectivePriceAppService.GetWorkingLocalCurrencyUnitIdAsync();
        if (countryUnitId is not { } targetUnitId)
        {
            return results;   // ülke (rebase hedefi) birimi yok → net maliyet hesaplanamaz (boş)
        }

        // Değerleme: ülke birimine rebase'li efektifler (SELL bacağı — reçete kararı). TEK çağrı.
        var valuation = await _effectivePriceAppService.GetValuationByBaseAsync(targetUnitId);
        var sellByUnit = valuation.ToDictionary(v => v.Id, v => v.Sell);
        var codeByUnit = valuation.ToDictionary(v => v.Id, v => v.CurrencyUnitCode);
        var countryCode = valuation.FirstOrDefault()?.BaseCurrencyCode ?? string.Empty;

        var catalog = await LoadRecipeCatalogAsync(allLines);

        for (var setIndex = 0; setIndex < lineSets.Count; setIndex++)
        {
            var lines = lineSets[setIndex];
            if (lines.Count == 0)
            {
                results[setIndex] = new RecipeSetCost(null, countryCode, false);
                continue;
            }

            // Türev SelectedLines ordinal çözümü için ClientKey→pozisyon (satırlar LineOrder sırasında).
            var ordinalByClientKey = new Dictionary<Guid, int>();
            for (var idx = 0; idx < lines.Count; idx++)
            {
                ordinalByClientKey[lines[idx].ClientKey] = idx;
            }

            var inputs = lines.Select(l => BuildCostInput(l, catalog, ordinalByClientKey)).ToList();
            var result = _recipeCostCalculator.Compute(inputs, sellByUnit, countryCode);

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var r = result.Lines[i];
                line.LineCost = r.Cost;
                line.LineCostMissingRate = r.MissingRate;
                line.Total = r.Total;
                line.PayTotal = r.PayTotal;
                line.AppliedBase = r.AppliedBase;
                line.RunningSubtotal = r.RunningSubtotal;
                line.MainUnitCode = line.ValuationUnitId is { } mu ? codeByUnit.GetValueOrDefault(mu, string.Empty) : string.Empty;
                line.PayUnitCode = line.PayUnitId is { } pu ? codeByUnit.GetValueOrDefault(pu, string.Empty) : string.Empty;
            }

            results[setIndex] = new RecipeSetCost(result.Net, countryCode, result.AnyMissingRate);
        }

        return results;
    }

    /// <summary>Graf düğümünden calculator girdisi kurar — katalog canlı verisi (metal adet→gram, parasal giriş
    /// fiyatı) <paramref name="catalog"/>'dan çözülür; eksikse 0 (satır sonra MissingRate/0 verir).</summary>
    private static RecipeLineCostInput BuildCostInput(
        ProductRecipeLineGraphDto l, RecipeCatalogData catalog, Dictionary<Guid, int> ordinalByClientKey)
    {
        var isQuantity = false;
        var stableQuantity = 0m;
        var priceByQuantity = false;
        var entryPrice = 0m;
        var laborByQuantity = false;

        if (l.ComponentType == RecipeComponentType.CatalogCommodity && l.CommodityId is { } commodityId)
        {
            if (l.CommodityProcessType == ProcessType.Metal && catalog.Metals.TryGetValue(commodityId, out var m))
            {
                isQuantity = m.IsQuantity;
                stableQuantity = m.StableQuantity;
                laborByQuantity = m.LaborByQuantity;
            }
            else if (l.CommodityProcessType == ProcessType.Jewelry && catalog.Jewelries.TryGetValue(commodityId, out var j))
            {
                entryPrice = j.EntryPrice;
                priceByQuantity = j.PriceByQuantity;
            }
            else if (l.CommodityProcessType == ProcessType.Stone && catalog.Stones.TryGetValue(commodityId, out var s))
            {
                entryPrice = s.EntryPrice;
                priceByQuantity = s.PriceByQuantity;
            }
        }

        // Türev SelectedLines: seçili kaynak ClientKey'leri → pozisyon ordinal'leri (calculator upstream doğrular).
        var derivedOrdinals = l.ComponentType == RecipeComponentType.Service
            && l.DerivedBaseMode == RecipeDerivedBaseMode.SelectedLines
            ? l.DerivedSourceKeys.Where(ordinalByClientKey.ContainsKey).Select(k => ordinalByClientKey[k]).ToList()
            : new List<int>();

        return new RecipeLineCostInput(
            l.ComponentType,
            l.CommodityProcessType,
            l.Quantity,
            l.Amount,
            l.Factor,
            isQuantity,
            stableQuantity,
            priceByQuantity,
            entryPrice,
            l.ValuationUnitId,
            l.PaymentType,
            l.PayFactor,
            l.PayUnitId,
            laborByQuantity,
            l.ManualAmount,
            l.ManualUnitId,
            l.DerivedBaseMode,
            l.DerivedOperation,
            l.DerivedOperand,
            derivedOrdinals);
    }

    /// <summary>Reçetede geçen katalog kayıtlarının hesaba giren canlı verisini (metal adet→gram; parasal giriş
    /// fiyatı) TEK batch'te yükler. Filtreler kapalı (host/global katalog kaydı da çözülsün — salt-okuma).</summary>
    private async Task<RecipeCatalogData> LoadRecipeCatalogAsync(List<ProductRecipeLineGraphDto> lines)
    {
        Guid[] IdsOfFamily(ProcessType family)
        {
            return lines
                .Where(l => l.ComponentType == RecipeComponentType.CatalogCommodity
                    && l.CommodityProcessType == family
                    && l.CommodityId is not null)
                .Select(l => l.CommodityId!.Value)
                .Distinct()
                .ToArray();
        }

        var metalIds = IdsOfFamily(ProcessType.Metal);
        var jewelryIds = IdsOfFamily(ProcessType.Jewelry);
        var stoneIds = IdsOfFamily(ProcessType.Stone);

        var metals = new Dictionary<Guid, MetalCatalogCost>();
        var jewelries = new Dictionary<Guid, PricedCatalogCost>();
        var stones = new Dictionary<Guid, PricedCatalogCost>();

        using (_dataFilter.Disable<IMultiTenant>())
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            if (metalIds.Length > 0)
            {
                metals = (await _asyncExecuter.ToListAsync(
                        (await _metalRepository.GetQueryableAsync()).Where(m => metalIds.Contains(m.Id))))
                    .ToDictionary(m => m.Id, m => new MetalCatalogCost(
                        m.IsQuantity, m.StableQuantity, m.LaborType == MetalLaborType.Quantity));
            }

            if (jewelryIds.Length > 0)
            {
                jewelries = (await _asyncExecuter.ToListAsync(
                        (await _jewelryRepository.GetQueryableAsync()).Where(j => jewelryIds.Contains(j.Id))))
                    .ToDictionary(j => j.Id, j => new PricedCatalogCost(j.EntryPrice, j.PriceByQuantity));
            }

            if (stoneIds.Length > 0)
            {
                stones = (await _asyncExecuter.ToListAsync(
                        (await _stoneRepository.GetQueryableAsync()).Where(s => stoneIds.Contains(s.Id))))
                    .ToDictionary(s => s.Id, s => new PricedCatalogCost(s.EntryPrice, s.PriceByQuantity));
            }
        }

        return new RecipeCatalogData(metals, jewelries, stones);
    }

    /// <summary>Kaydedilmiş türev SelectedLines satırlarının kalıcı kaynak-Id CSV'sini (bir satır setinde), o setin
    /// TAZE ClientKey'lerine çevirir (UI round-trip + canlı hesap ordinal çözümü). <paramref name="sourceCsvByLineId"/>
    /// = satırId → '|'-join kaynak-Id CSV. Kaydetme referans-bütünlüğü sağladığından çözülemeyen parça sessizce atlanır.
    /// Satırların benzersiz kalıcı Id'si olmalı (yalnız kaydedilmiş satır seti; klon/yeni satır yolu AYRI çözer).</summary>
    public static void ResolveDerivedSourceKeys(
        List<ProductRecipeLineGraphDto> lines, IReadOnlyDictionary<Guid, string> sourceCsvByLineId)
    {
        if (sourceCsvByLineId.Count == 0)
        {
            return;
        }

        var clientKeyById = lines.ToDictionary(l => l.Id, l => l.ClientKey);
        foreach (var l in lines.Where(x => x.ComponentType == RecipeComponentType.Service))
        {
            if (!sourceCsvByLineId.TryGetValue(l.Id, out var csv))
            {
                continue;
            }

            l.DerivedSourceKeys = csv
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => Guid.TryParse(part, out var srcId) && clientKeyById.TryGetValue(srcId, out var ck)
                    ? ck
                    : (Guid?)null)
                .Where(ck => ck.HasValue)
                .Select(ck => ck!.Value)
                .ToList();
        }
    }

    /// <summary>Türev satır referans-bütünlüğü (kaydetmeden ÖNCE, fail-fast; ERP + N11 ortak): SelectedLines satırının
    /// seçili kaynakları BOŞ olamaz, hepsi mevcut (silinmemiş) KARDEŞ satır olmalı ve yalnız kendinden ÖNCEKİ satırları
    /// (küçük LineOrder) referanslamalı → döngüsüz + kendine-referans yok. AllAbove kaynak gerektirmez.
    /// <paramref name="survivors"/>'ın LineOrder'ı 0..n-1 yeniden-numaralı (benzersiz pozisyon) olmalı.</summary>
    public static void ValidateDerivedReferences(List<ProductRecipeLineGraphDto> survivors)
    {
        var orderByClientKey = survivors.ToDictionary(x => x.ClientKey, x => x.LineOrder);

        foreach (var l in survivors.Where(x => x.ComponentType == RecipeComponentType.Service
            && x.DerivedBaseMode == RecipeDerivedBaseMode.SelectedLines))
        {
            if (l.DerivedSourceKeys == null || l.DerivedSourceKeys.Count == 0)
            {
                throw new BusinessException("TradeXpress:ProductRecipeLine:DerivedNeedsSelection");
            }

            foreach (var key in l.DerivedSourceKeys)
            {
                if (!orderByClientKey.TryGetValue(key, out var sourceOrder) || sourceOrder >= l.LineOrder)
                {
                    // kaynak yok (silinmiş/yabancı) YA DA kendini/sonrasını referanslıyor → döngü/geçersiz.
                    throw new BusinessException("TradeXpress:ProductRecipeLine:DerivedRefMustBeUpstream");
                }
            }
        }
    }

    private sealed record MetalCatalogCost(bool IsQuantity, decimal StableQuantity, bool LaborByQuantity);
    private sealed record PricedCatalogCost(decimal EntryPrice, bool PriceByQuantity);
    private sealed record RecipeCatalogData(
        Dictionary<Guid, MetalCatalogCost> Metals,
        Dictionary<Guid, PricedCatalogCost> Jewelries,
        Dictionary<Guid, PricedCatalogCost> Stones);
}

/// <summary>Bir reçete satır setinin net-maliyet özeti — net toplam (null ⇔ hesaplanamadı) + ülke birim kodu +
/// eksik-kur bayrağı. Satır-başı alanlar DTO'larda yerinde doldurulur.</summary>
public sealed record RecipeSetCost(decimal? NetCost, string NetCostCurrency, bool NetCostMissingRate);
