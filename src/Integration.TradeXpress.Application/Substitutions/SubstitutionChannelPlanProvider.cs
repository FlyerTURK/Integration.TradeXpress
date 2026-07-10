using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Vouchers;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil M4 köprüsünün KANAL-AGNOSTİK çekirdek servisi — N11 ve Trendyol adaptörleri AYNI sağlayıcıyı kullanır:
/// <list type="number">
///   <item><b>Tek motor zinciri:</b> hesap <see cref="ISubstitutionCalculationAppService"/>'ten yeniden koşulur
///   (paralel hesap yolu YOK), başarılılar Rank sırasıyla saf <see cref="SubstitutionStockItemPlanner"/>'a verilir.</item>
///   <item><b>Katalog çözümü:</b> plandaki madenler tek batch'te yüklenir (host kataloğu tenant altında da
///   görünsün diye IMultiTenant filtresi kapalı — RecipeCostPopulator deseni).</item>
///   <item><b>Statik yardımcılar:</b> plan↔mevcut değer diff'i + plan reçetesi → graf DTO dönüşümü — iki kanal
///   adaptörünün ortak metin/eşleme mantığı burada, kanal graf tipleri adaptörde kalır.</item>
///   <item><b>Uygula orkestrasyonu:</b> <see cref="ApplyAsync{THeader}"/> — plan kur → "Kombinasyon" özelliği
///   diff/upsert → StockItem'lara reçete + paket stoğu akışının TEK gövdesi; persist/graf işleri
///   <c>VariantSetReconciler</c> emsaliyle delegate'lerle adaptöre bırakılır.</item>
/// </list>
/// Fiyat YAZMAZ — reçete kurulur, fiyat mevcut maliyet zincirinden (RecipeCostPopulator → marj → türetilmiş) doğar.
/// </summary>
public class SubstitutionChannelPlanProvider : ITransientDependency
{
    private readonly ISubstitutionCalculationAppService _calculationAppService;
    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IDataFilter _dataFilter;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public SubstitutionChannelPlanProvider(
        ISubstitutionCalculationAppService calculationAppService,
        IRepository<Metal, Guid> metalRepository,
        IDataFilter dataFilter,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _calculationAppService = calculationAppService;
        _metalRepository       = metalRepository;
        _dataFilter            = dataFilter;
        _asyncExecuter         = asyncExecuter;
    }

    /// <summary>Hesabı koşar + planı kurar + plandaki madenleri çözer. Başarılı kombinasyon yoksa
    /// planlayıcı <c>NoSuccessfulCombination</c> fırlatır (fail-fast).</summary>
    public virtual async Task<SubstitutionChannelPlanContext> BuildAsync(SubstitutionApplyInput input)
    {
        Check.NotNull(input, nameof(input));

        if (input.TopN <= 0)
        {
            // Varyant sayısını kullanıcı seçer (konsept karar 5) — sessizce "tümü" üretmek sürpriz olur.
            throw new BusinessException("TradeXpress:Substitution:TopNInvalid");
        }

        var calculation = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId = input.SubstitutionGroupId,
            TargetQuantity      = input.TargetQuantity,
            TopN                = input.TopN,
            BranchId            = input.BranchId,
            VaultId             = input.VaultId,
        });

        var successful = calculation.Trials
            .Where(t => t.Success && t.Rank is not null)
            .OrderBy(t => t.Rank)
            .Select(ToPlanCombination)
            .ToList();

        var plan = SubstitutionStockItemPlanner.Build(new SubstitutionStockItemPlanInput(
            calculation.ToleranceType, calculation.ToleranceValue, input.TopN, successful));

        var metalById = await LoadPlanMetalsAsync(plan);
        return new SubstitutionChannelPlanContext(plan, metalById);
    }

    /// <summary>
    /// KANAL-AGNOSTİK uygula orkestrasyonu — N11/Trendyol <c>ApplySubstitutionAsync</c>'lerinin TEK gövdesi:
    /// plan kur (<see cref="BuildAsync"/>) → "Kombinasyon" özelliği + değer diff'i → upsert planını adaptöre
    /// persist ettir (adaptör MEVCUT persist + kartezyen reconcile yolundan geçirir; guard'lar dahil, paralel
    /// kayıt yolu YOK) → her kombinasyon değerini imzasında taşıyan StockItem'lara reçete + paket stoğu uygula.
    /// Kanal graf tipleri (attribute/değer/StockItem entity + DTO'ları) delegate'lerle adaptörde kalır
    /// (<c>VariantSetReconciler</c> deseni). Girdi YALNIZ "Kombinasyon" özelliğini içerir — kullanıcının diğer
    /// özellikleri adaptörün girdi-bazlı upsert sözleşmesi gereği el değmeden kalır.
    /// </summary>
    /// <param name="loadChannelAttributesAsync">Kanal-ürünün TÜM özellik başlıkları (mevcut "Kombinasyon"u bulmak
    /// + yeni özellik DisplayOrder'ını türetmek için).</param>
    /// <param name="loadCombinationValuesAsync">Verilen özelliğin mevcut değerleri (Id + metin) — diff girdisi.</param>
    /// <param name="persistAndReconcileAsync">Upsert planını kanal DTO'suna çevirip MEVCUT
    /// SaveAttributesAndReconcileAsync yolundan geçirir; upsert sonrası GERÇEK attribute id + silinmemiş değer
    /// id'lerini GİRDİ SIRASIYLA döner (binding i ↔ ValueIds[i]).</param>
    /// <param name="loadCombinationHeadersAsync">Kombinasyon imzalı StockItem başlıkları (reconcile SONRASI durum).</param>
    /// <param name="signatureOf">Başlığın kombinasyon imzası ("{AttributeId}={ValueId}|...").</param>
    /// <param name="applyCombinationToHeaderAsync">Eşleşen başlığa paket stoğu + TAZE reçete satırlarını yazar.</param>
    public virtual async Task<SubstitutionApplyResultDto> ApplyAsync<THeader>(
        SubstitutionApplyInput input,
        Func<Task<List<SubstitutionChannelAttributeRef>>> loadChannelAttributesAsync,
        Func<Guid, Task<List<(Guid Id, string Value)>>> loadCombinationValuesAsync,
        Func<SubstitutionCombinationAttributeUpsert, Task<(Guid AttributeId, List<Guid> ValueIds)>> persistAndReconcileAsync,
        Func<Task<List<THeader>>> loadCombinationHeadersAsync,
        Func<THeader, string> signatureOf,
        Func<THeader, int, List<ProductRecipeLineGraphDto>, Task> applyCombinationToHeaderAsync)
    {
        var context = await BuildAsync(input);
        var plan = context.Plan;

        // 1) "Kombinasyon" özelliği + değer diff'i — eşleşen değer id'leri korunur (StockItem imzaları yaşar).
        var channelAttributes = await loadChannelAttributesAsync();
        var combinationAttribute = channelAttributes
            .FirstOrDefault(a => a.Name == SubstitutionBridgeConsts.CombinationAttributeName);
        var existingValues = combinationAttribute is null
            ? new List<(Guid Id, string Value)>()
            : await loadCombinationValuesAsync(combinationAttribute.Id);
        var diff = DiffCombinationValues(plan, existingValues);

        // 2) Upsert planı → adaptörün mevcut persist + kartezyen reconcile yolu (TooManyAttributes guard'ı dahil).
        var upsert = BuildCombinationAttributeUpsert(combinationAttribute, channelAttributes, diff);
        var (attributeId, valueIds) = await persistAndReconcileAsync(upsert);

        // 3) Kombinasyon satırlarına reçete + paket stoğu yaz (fiyat/marj/override KULLANICININ — dokunulmaz).
        var result = new SubstitutionApplyResultDto
        {
            ToleranceNotice        = plan.ToleranceNotice,
            CombinationAttributeId = attributeId,
        };

        var headers = await loadCombinationHeadersAsync();
        for (var i = 0; i < diff.Bindings.Count; i++)
        {
            var item = diff.Bindings[i].Item;
            var pairToken = $"{attributeId}={valueIds[i]}";
            var matched = headers.Where(h => SignatureHasPair(signatureOf(h), pairToken)).ToList();

            foreach (var header in matched)
            {
                // Her başlığa TAZE reçete DTO listesi (Id boş = insert) — aynı plan kaydı birden çok StockItem'a uygulanabilir.
                await applyCombinationToHeaderAsync(header, item.PackageCount, BuildRecipeLineDtos(item, context.MetalById));
            }

            result.Items.Add(new SubstitutionAppliedCombinationDto
            {
                Rank           = item.Rank,
                IsPrimary      = item.IsPrimary,
                ValueText      = item.ValueText,
                PackageCount   = item.PackageCount,
                StockItemCount = matched.Count,
            });
        }

        return result;
    }

    /// <summary>Köprünün yönettiği "Kombinasyon" özelliğinin KANAL-NÖTR upsert planını kurar: plan değerleri
    /// Rank sırasıyla (DisplayOrder = sıra; 0 = ANA varyant) + plan dışı kalan mevcut değerler IsDeleted işaretli.
    /// Özellik yoksa DisplayOrder mevcut özelliklerin sonuna eklenir.</summary>
    private static SubstitutionCombinationAttributeUpsert BuildCombinationAttributeUpsert(
        SubstitutionChannelAttributeRef? combinationAttribute,
        List<SubstitutionChannelAttributeRef> channelAttributes,
        SubstitutionCombinationValueDiff diff)
    {
        var values = new List<SubstitutionCombinationValueUpsert>(diff.Bindings.Count + diff.DeletedValueIds.Count);
        for (var i = 0; i < diff.Bindings.Count; i++)
        {
            values.Add(new SubstitutionCombinationValueUpsert(
                diff.Bindings[i].ExistingValueId, diff.Bindings[i].Item.ValueText, DisplayOrder: i, IsDeleted: false));
        }

        foreach (var deletedId in diff.DeletedValueIds)
        {
            values.Add(new SubstitutionCombinationValueUpsert(
                deletedId, string.Empty, DisplayOrder: 0, IsDeleted: true));
        }

        return new SubstitutionCombinationAttributeUpsert(
            combinationAttribute?.Id ?? Guid.Empty,
            SubstitutionBridgeConsts.CombinationAttributeName,
            combinationAttribute?.DisplayOrder
                ?? (channelAttributes.Count == 0 ? 0 : channelAttributes.Max(a => a.DisplayOrder) + 1),
            values);
    }

    /// <summary>İmza "{AttributeId}={ValueId}" çiftini TAM segment eşleşmesiyle arar (substring yanlış-pozitifi yok).</summary>
    private static bool SignatureHasPair(string signature, string pairToken)
    {
        return signature
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Contains(pairToken, StringComparer.Ordinal);
    }

    /// <summary>Plan kayıtlarını "Kombinasyon" özelliğinin MEVCUT değerleriyle eşler (normalize metin bazlı —
    /// persist edilen değer NormalizeAsName/TitleCase'ten geçmiştir). Eşleşen → mevcut değer id'siyle güncellenir
    /// (imza korunur → StockItem + kullanıcı override'ı yaşar); eşleşmeyen mevcutlar → silinecekler listesi.</summary>
    public static SubstitutionCombinationValueDiff DiffCombinationValues(
        SubstitutionStockItemPlan plan, IReadOnlyList<(Guid Id, string Value)> existingValues)
    {
        var idByNormalizedValue = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var (id, value) in existingValues)
        {
            // Aynı metinli bayat çift kayıt olursa ilki eşleşir, kalanı silinir (savunmacı).
            idByNormalizedValue.TryAdd(value, id);
        }

        var bindings = new List<SubstitutionCombinationValueBinding>(plan.Items.Count);
        var matchedIds = new HashSet<Guid>();
        foreach (var item in plan.Items)
        {
            var normalized = item.ValueText.NormalizeAsName();
            var existingId = idByNormalizedValue.TryGetValue(normalized, out var id) && matchedIds.Add(id)
                ? id
                : Guid.Empty;
            bindings.Add(new SubstitutionCombinationValueBinding(item, existingId));
        }

        var deletedIds = existingValues
            .Select(v => v.Id)
            .Where(id => !matchedIds.Contains(id))
            .ToList();

        return new SubstitutionCombinationValueDiff(bindings, deletedIds);
    }

    /// <summary>Plan reçete satırlarını kanal reçete graf DTO'larına çevirir (metal satırı: metal bacağı
    /// Factor/FollowingUnit + işçilik bacağı EntryLabor — hesap beslemesindeki parça-maliyet girdisiyle birebir,
    /// böylece StockItem NetCost'u hesap maliyetiyle aynı motordan aynı sonucu üretir). Her çağrı TAZE DTO listesi
    /// döner (Id boş = insert) — aynı plan kaydı birden çok StockItem'a uygulanabilir.</summary>
    public static List<ProductRecipeLineGraphDto> BuildRecipeLineDtos(
        SubstitutionStockItemPlanItem item, IReadOnlyDictionary<Guid, Metal> metalById)
    {
        var lines = new List<ProductRecipeLineGraphDto>(item.RecipeLines.Count);
        for (var i = 0; i < item.RecipeLines.Count; i++)
        {
            var planLine = item.RecipeLines[i];
            if (!metalById.TryGetValue(planLine.MetalId, out var metal))
            {
                throw new BusinessException("TradeXpress:Substitution:MetalNotFound");
            }

            lines.Add(new ProductRecipeLineGraphDto
            {
                LineOrder            = i,
                ComponentType        = RecipeComponentType.CatalogCommodity,
                CommodityProcessType = ProcessType.Metal,
                CommodityId          = metal.Id,
                Quantity             = planLine.Count,
                Amount               = planLine.Count * metal.StableQuantity,
                Factor               = metal.Factor,
                ValuationUnitId      = metal.FollowingUnitId,
                PaymentType          = ProcessPaymentType.Normal,
                PayFactor            = metal.EntryLabor,
                PayUnitId            = metal.EntryLaborUnitId,
            });
        }

        return lines;
    }

    private static SubstitutionPlanCombination ToPlanCombination(SubstitutionTrialDto trial)
    {
        return new SubstitutionPlanCombination(
            trial.Rank!.Value,
            trial.PackageCount,
            trial.Lines
                .Select(l => new SubstitutionPlanCombinationLine(l.MetalId, l.MetalCode, l.PieceWeight, l.Count))
                .ToList());
    }

    /// <summary>Plandaki madenleri tek batch'te yükler — host (TenantId=null) katalog kaydı tenant altında da
    /// görünmeli (SubstitutionCalculationAppService/RecipeCostPopulator ile aynı desen). Eksik maden fail-fast.</summary>
    private async Task<IReadOnlyDictionary<Guid, Metal>> LoadPlanMetalsAsync(SubstitutionStockItemPlan plan)
    {
        var metalIds = plan.Items
            .SelectMany(i => i.RecipeLines)
            .Select(l => l.MetalId)
            .Distinct()
            .ToList();

        Dictionary<Guid, Metal> metalById;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var metals = await _asyncExecuter.ToListAsync(
                (await _metalRepository.GetQueryableAsync()).Where(m => metalIds.Contains(m.Id)));
            metalById = metals.ToDictionary(m => m.Id);
        }

        if (metalById.Count != metalIds.Count)
        {
            throw new BusinessException("TradeXpress:Substitution:MetalNotFound");
        }

        return metalById;
    }
}

/// <summary>Köprü bağlamı — nötr plan + plandaki madenlerin çözülmüş katalog kayıtları.</summary>
public sealed record SubstitutionChannelPlanContext(
    SubstitutionStockItemPlan Plan,
    IReadOnlyDictionary<Guid, Metal> MetalById);

/// <summary>Değer diff sonucu — plan↔mevcut değer eşlemeleri + artık plan dışı kalan (silinecek) değer id'leri.</summary>
public sealed record SubstitutionCombinationValueDiff(
    IReadOnlyList<SubstitutionCombinationValueBinding> Bindings,
    IReadOnlyList<Guid> DeletedValueIds);

/// <summary>Tek plan kaydının mevcut değere bağlanması — <see cref="ExistingValueId"/> boş = yeni değer.</summary>
public sealed record SubstitutionCombinationValueBinding(
    SubstitutionStockItemPlanItem Item,
    Guid ExistingValueId);

/// <summary>Kanal özellik başlığının NÖTR görünümü — adaptör kendi attribute entity'sinden kurar
/// (orkestrasyon "Kombinasyon"u adıyla bulur + yeni özellik sırasını türetir).</summary>
public sealed record SubstitutionChannelAttributeRef(Guid Id, string Name, int DisplayOrder);

/// <summary>Köprünün yönettiği "Kombinasyon" özelliğinin KANAL-NÖTR upsert planı — adaptör kendi kanal
/// attribute DTO'suna çevirip MEVCUT persist + reconcile yolundan geçirir. <see cref="AttributeId"/> boş =
/// özellik ilk kez oluşturuluyor; <see cref="Values"/> plan değerleri Rank sırasıyla + silinecek işaretliler.</summary>
public sealed record SubstitutionCombinationAttributeUpsert(
    Guid AttributeId,
    string Name,
    int DisplayOrder,
    IReadOnlyList<SubstitutionCombinationValueUpsert> Values);

/// <summary>Upsert değer satırı — <see cref="IsDeleted"/> true ise yalnız <see cref="Id"/> anlamlıdır
/// (plan dışı kalan mevcut değer silinir; mevcut davranışla birebir).</summary>
public sealed record SubstitutionCombinationValueUpsert(
    Guid Id,
    string ValueText,
    int DisplayOrder,
    bool IsDeleted);
