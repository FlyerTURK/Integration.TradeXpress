using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil bağlamı yükleyicisi: kombinasyonda geçen madenlerin katalog kayıtları + işçilik sözlükleri
/// (seçili varyantın işçiliği, varyantsız satırlar için ana-varyant fallback'i).
///
/// <para><b>Neden AYRI servis:</b> bu yükleme üç tüketicinin ortak ihtiyacı — kanal planı köprüsü
/// (<see cref="SubstitutionChannelPlanProvider"/>), ERP varyant materyalizasyonu ve hesap servisinin reçete
/// önizlemesi. Provider'ın İÇİNDE kalsaydı hesap servisi onu kullanmak için provider'a bağımlı olurdu; provider
/// ise zaten hesap servisini enjekte ediyor → DÖNGÜSEL BAĞIMLILIK. Yükleyici bağımsız olduğu için üçü de onu
/// çağırır, kimse kimseye bağlanmaz (2026-07-27).</para>
///
/// <para><b>Guard'lar:</b> maden katalogları host kaydı olabildiğinden tenant/company filtreleri KAPALI okunur
/// (ComputeUnitCostsAsync ile aynı desen) — aksi hâlde tenant bağlamında host madeni "yok" görünürdü.</para>
/// </summary>
public class SubstitutionPlanContextLoader : ITransientDependency
{
    /// <summary>EntityVariant sahip-tipi guard'ı — işçilik join'i yalnız Metal varyantlarına daralır.</summary>
    private const string MetalEntityName = "Metal";

    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IRepository<EntityVariant, Guid> _entityVariantRepository;
    private readonly IRepository<MetalVariantDetail, Guid> _metalVariantDetailRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;

    public SubstitutionPlanContextLoader(
        IRepository<Metal, Guid> metalRepository,
        IRepository<EntityVariant, Guid> entityVariantRepository,
        IRepository<MetalVariantDetail, Guid> metalVariantDetailRepository,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter)
    {
        _metalRepository = metalRepository;
        _entityVariantRepository = entityVariantRepository;
        _metalVariantDetailRepository = metalVariantDetailRepository;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
    }

    /// <summary>Maden + işçilik bağlamını doğrudan id kümelerinden yükler. Bir maden katalogda bulunamazsa
    /// FAIL-FAST: eksik madenle kurulan reçete sessizce yanlış maliyet üretirdi.</summary>
    public virtual async Task<SubstitutionChannelPlanContext> LoadAsync(
        IReadOnlyCollection<Guid> metalIdSet, IReadOnlyCollection<Guid> variantIdSet)
    {
        var metalIds = metalIdSet.ToList();
        var variantIds = variantIdSet.ToList();

        Dictionary<Guid, Metal> metalById;
        Dictionary<Guid, SubstitutionPlanLabor> laborByVariantId;
        Dictionary<Guid, SubstitutionPlanLabor> mainLaborByMetalId;

        using (_dataFilter.Disable<IMultiTenant>())
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            var metals = await _asyncExecuter.ToListAsync(
                (await _metalRepository.GetQueryableAsync()).Where(m => metalIds.Contains(m.Id)));
            metalById = metals.ToDictionary(m => m.Id);

            var variantsQuery = await _entityVariantRepository.GetQueryableAsync();
            var detailsQuery = await _metalVariantDetailRepository.GetQueryableAsync();

            // Seçilen varyantların işçiliği (varyant-anahtarlı — satır hangi varyantı seçtiyse o).
            var variantLabors = await _asyncExecuter.ToListAsync(
                from v in variantsQuery
                join d in detailsQuery on v.Id equals d.EntityVariantId
                where v.EntityName == MetalEntityName && variantIds.Contains(v.Id)
                select new { v.Id, d.EntryLabor, d.EntryLaborUnitId }
            );
            laborByVariantId = variantLabors.ToDictionary(
                x => x.Id,
                x => new SubstitutionPlanLabor(x.EntryLabor, x.EntryLaborUnitId));

            // Ana-varyant fallback'i — varyantsız (legacy) satırlar statüko yolunda kalır.
            var mainLabors = await _asyncExecuter.ToListAsync(
                from v in variantsQuery
                join d in detailsQuery on v.Id equals d.EntityVariantId
                where v.IsMain && v.EntityName == MetalEntityName && metalIds.Contains(v.EntityId)
                select new { v.EntityId, d.EntryLabor, d.EntryLaborUnitId }
            );
            mainLaborByMetalId = mainLabors.ToDictionary(
                x => x.EntityId,
                x => new SubstitutionPlanLabor(x.EntryLabor, x.EntryLaborUnitId));
        }

        if (metalById.Count != metalIds.Count)
        {
            throw new BusinessException("TradeXpress:Substitution:MetalNotFound");
        }

        return new SubstitutionChannelPlanContext(Plan: null, metalById, laborByVariantId, mainLaborByMetalId);
    }

    /// <summary>
    /// Kombinasyondan REÇETE SATIRLARI üretir (DTO). <c>SubstitutionVariantMaterializer</c>'ın kayıt anında
    /// ENTITY olarak kurduğu satırlarla AYNI matematiktir — metal bacağı (Factor/FollowingUnit) + işçilik bacağı
    /// (EntryLabor), miktar = adet, tutar = adet × parça gramı.
    ///
    /// <para><b>Neden burada:</b> reçeteyi iki taraf da kuruyor — sunucu kalıcılaştırırken, ürün formu ise
    /// kaydetmeden ÖNİZLERKEN. Matematik iki yerde ayrı yazılsaydı önizlemedeki maliyet kayıttakinden saparlardı;
    /// kullanıcı "kaydedince fiyat değişti" derdi. Bu yüzden dönüşüm tek yerde durur ve saf/statiktir.</para>
    /// </summary>
    public static List<ProductRecipeLineGraphDto> BuildRecipeLineDtos(
        SubstitutionTrialDto trial, SubstitutionChannelPlanContext context)
    {
        var lines = new List<ProductRecipeLineGraphDto>(trial.Lines.Count);

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

            lines.Add(new ProductRecipeLineGraphDto
            {
                LineOrder            = i,
                ComponentType        = RecipeComponentType.CatalogCommodity,
                CommodityProcessType = ProcessType.Metal,
                CommodityId          = metal.Id,
                CommodityVariantId   = trialLine.VariantId,
                Quantity             = trialLine.Count,
                Amount               = trialLine.Count * metal.StableQuantity,
                Factor               = metal.Factor,
                ValuationUnitId      = metal.FollowingUnitId,
                PaymentType          = ProcessPaymentType.Normal,
                PayFactor            = labor?.EntryLabor ?? 0m,
                PayUnitId            = labor?.EntryLaborUnitId,
            });
        }

        return lines;
    }
}
