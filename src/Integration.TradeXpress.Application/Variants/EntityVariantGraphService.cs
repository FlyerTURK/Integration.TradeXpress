using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels.Variants;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// Agnostik varyant grafı servisi — SAHİP AppService'lerin (Good, Product, Metal…) DELEGE ettiği tek nokta. Nitelik/değer
/// graf-diff → kartezyen senkron → çekirdek varyant özelleştirme (Barkod/Stok/Açıklama/Aktif) + persistsiz üretim önizlemesi
/// + graf okuma/silme — hepsi (EntityName, EntityId) üzerinden. Sahip AppService yalnız 3-4 satır delege eder (DRY).
/// SUNUCU-İÇİ İÇ YARDIMCI (ApplicationService DEĞİL — 2026-07-26): entityName/entityId keyfi client'tan gelmemeli
/// (sahip AppService güvenlik sınırını tutar). ApplicationService mirası bu iç yardımcıya AppService interceptor'larını
/// takıyordu — audit, Func parametresini (saveExtensionAsync) JSON'a serileştirmeye çalışıp her ürün kaydında
/// NotSupportedException logluyordu; validation da aynı delegate'te patlayıp [DisableValidation] yarası gerektirmişti.
/// Düz ITransientDependency ile interceptor katmanı hiç kurulmaz (CommodityAgnosticGraph ile aynı desen);
/// UoW/audit sorumluluğu delege eden SAHİP AppService'te kalır — doğru irtifa da orası.
/// </summary>
public interface IEntityVariantGraphService
{
    /// <summary>Grafı saklar: nitelik/değer diff → synchronizer kartezyen → çekirdek varyant özelleştirmeleri. Sahip entity
    /// zaten kaydedilmiş olmalı (Id + CompanyId + ownerName sahipten okunur). <paramref name="saveExtensionAsync"/>:
    /// her varyant çözülüp çekirdeği kaydedildikten SONRA (dto, DB-varyant-Id) ile çağrılır — sahip entity-ÖZEL
    /// uzantısını (ör. GoodVariantDetail fiyat/stok) o DB varyanta bağlar. <paramref name="variants"/> kovaryant
    /// (IReadOnlyList) → sahip türetilmiş DTO listesini (List&lt;GoodVariantGraphDto&gt;) doğrudan geçebilir.</summary>
    Task SaveGraphAsync(
        string entityName, Guid entityId, Guid? companyId, string ownerName,
        List<EntityAttributeGraphDto> attributes, IReadOnlyList<EntityVariantGraphDto> variants,
        Func<EntityVariantGraphDto, Guid, Task>? saveExtensionAsync = null);

    /// <summary>Sahip entity'nin varyant grafını okur (GetAsync projeksiyonu) — nitelikler + varyantlar (AttributeSummary dolu).</summary>
    Task<EntityVariantGraphResult> LoadGraphAsync(string entityName, Guid entityId);

    /// <summary>Persistsiz varyant önizlemesi — nitelik×değer kartezyeni → varyant graf satırları (DB'ye YAZMAZ).</summary>
    List<EntityVariantGraphDto> GenerateVariants(EntityVariantGenerateRequestDto input);

    /// <summary>Bir sahip entity'nin AKTİF varyantları — fiş satırı panelinin varyant combo'su için hafif seçenekler
    /// (Id/Code/IsMain; fiyat 0/null — varyant-başı fiyatı olan emtia (Good) kendi picker'ını kullanır). Ana varyant öncelikli.</summary>
    Task<List<CommodityVariantOptionDto>> GetActiveVariantOptionsAsync(string entityName, Guid entityId);

    /// <summary>Sahip entity'nin TÜM varyant grafını (bağ + varyant + değer + nitelik) siler — sahip silinmeden önce.
    /// <paramref name="deleteExtensionAsync"/>: varyantlar silinmeden ÖNCE varyant Id'leriyle çağrılır — sahip
    /// entity-özel uzantısını (ör. GoodVariantDetail) temizler (orphan önleme).</summary>
    Task DeleteForAsync(string entityName, Guid entityId, Func<IReadOnlyList<Guid>, Task>? deleteExtensionAsync = null);

    /// <summary>Sahip kayıtların ANA varyant Id'leri (EntityName + entityIds → entityId→mainVariantId) — liste önizlemesi için tek batch.</summary>
    Task<Dictionary<Guid, Guid>> GetMainVariantMapAsync(string entityName, IReadOnlyCollection<Guid> entityIds);
}

public class EntityVariantGraphService : IEntityVariantGraphService, ITransientDependency
{
    private readonly IRepository<EntityAttribute, Guid> _attributeRepository;
    private readonly IRepository<EntityAttributeValue, Guid> _valueRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<EntityVariantAttributeValue, Guid> _linkRepository;
    private readonly EntityVariantManager _variantManager;
    private readonly EntityVariantSynchronizer _variantSynchronizer;
    private readonly IDataFilter _dataFilter;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public EntityVariantGraphService(
        IRepository<EntityAttribute, Guid> attributeRepository,
        IRepository<EntityAttributeValue, Guid> valueRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<EntityVariantAttributeValue, Guid> linkRepository,
        EntityVariantManager variantManager,
        EntityVariantSynchronizer variantSynchronizer,
        IDataFilter dataFilter,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _attributeRepository = attributeRepository;
        _valueRepository = valueRepository;
        _variantRepository = variantRepository;
        _linkRepository = linkRepository;
        _variantManager = variantManager;
        _variantSynchronizer = variantSynchronizer;
        _dataFilter = dataFilter;
        _asyncExecuter = asyncExecuter;
    }

    public async Task SaveGraphAsync(
        string entityName, Guid entityId, Guid? companyId, string ownerName,
        List<EntityAttributeGraphDto> attributes, IReadOnlyList<EntityVariantGraphDto> variants,
        Func<EntityVariantGraphDto, Guid, Task>? saveExtensionAsync = null)
    {
        var valueMap = await SaveAttributesAsync(entityName, entityId, companyId, attributes);
        await _variantSynchronizer.SynchronizeAsync(entityName, entityId, companyId, ownerName);
        await ApplyVariantCustomizationsAsync(entityName, entityId, variants, valueMap, saveExtensionAsync);
    }

    public async Task<EntityVariantGraphResult> LoadGraphAsync(string entityName, Guid entityId)
    {
        var attributes = (await _asyncExecuter.ToListAsync(
                (await _attributeRepository.GetQueryableAsync())
                    .Where(a => a.EntityName == entityName && a.EntityId == entityId)))
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
            .ToList();

        var attributeIds = attributes.Select(a => a.Id).ToList();
        var values = attributeIds.Count == 0
            ? new List<EntityAttributeValue>()
            : (await _asyncExecuter.ToListAsync(
                    (await _valueRepository.GetQueryableAsync()).Where(v => attributeIds.Contains(v.EntityAttributeId))))
                .OrderBy(v => v.DisplayOrder).ThenBy(v => v.Value)
                .ToList();

        var variants = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == entityName && v.EntityId == entityId).OrderBy(v => v.Code));
        var variantIds = variants.Select(v => v.Id).ToList();
        var links = variantIds.Count == 0
            ? new List<EntityVariantAttributeValue>()
            : await _asyncExecuter.ToListAsync(
                (await _linkRepository.GetQueryableAsync()).Where(l => variantIds.Contains(l.EntityVariantId)));

        // Değer DTO'ları ÖNCE kur (ClientKey'leriyle) → hem nitelik projeksiyonu hem varyant CombinationKey AYNI ClientKey'leri
        // kullansın. Böylece yükleme sonrası otomatik regen MERGE'i (değer ClientKey imzası) yüklenen varyantı EŞLER → düzenleme korunur.
        var valueDtoByDbId = values.ToDictionary(
            v => v.Id,
            v => new EntityAttributeValueGraphDto { Id = v.Id, Value = v.Value, DisplayOrder = v.DisplayOrder });

        var variantDtos = variants.Select(v => new EntityVariantGraphDto
        {
            Id = v.Id,
            IsMain = v.IsMain,
            Code = v.Code,
            Name = v.Name,
            Description = v.Description,
            IsActive = v.IsActive,
            Barcode = v.Barcode,
            Gtin = v.Gtin,
            Mpn = v.Mpn,
            Oem = v.Oem,
            StockQuantity = v.StockQuantity,
            AttributeSummary = BuildAttributeSummary(v.Id, attributes, values, links),
            CombinationKey = BuildLoadedCombinationKey(v.Id, links, valueDtoByDbId),
        }).ToList();

        EnsureMainVariant(variantDtos);

        return new EntityVariantGraphResult
        {
            Attributes = attributes.Select(a => new EntityAttributeGraphDto
            {
                Id = a.Id,
                Name = a.Name,
                DisplayOrder = a.DisplayOrder,
                Values = values.Where(v => v.EntityAttributeId == a.Id).Select(v => valueDtoByDbId[v.Id]).ToList(),
            }).ToList(),
            Variants = variantDtos,
        };
    }

    // Eski kayıtlar (varyant sistemi eklenmeden ÖNCE oluşmuş) DB'de hiç varyant taşımaz → yükleme anında EN AZ BİR
    // ANAVARYANT göster (yeni-kayıt ApplyNewDefaults deseniyle simetrik). Id'siz + boş CombinationKey → kullanıcı
    // kaydettiğinde synchronizer bunu kalıcı ana varyanta çevirir. Varyantı OLAN kayıt etkilenmez.
    private static void EnsureMainVariant(List<EntityVariantGraphDto> variants)
    {
        if (variants.Count > 0)
        {
            return;
        }

        variants.Add(new EntityVariantGraphDto
        {
            IsMain = true,
            Code = EntityVariantConsts.MainVariantCode,
            Name = EntityVariantConsts.MainVariantName,
            IsActive = true,
        });
    }

    public List<EntityVariantGraphDto> GenerateVariants(EntityVariantGenerateRequestDto input)
    {
        var result = new List<EntityVariantGraphDto>();
        var axes = BuildGenerationAxes(input.Attributes);
        if (axes.Count == 0)
        {
            return result;
        }

        foreach (var combination in VariantCombinationEngine.BuildCartesian<GenerationAxisItem>(axes))
        {
            var valueNames = combination.Select(x => x.NormalizedValue).ToList();
            var summary = string.Join(", ", combination.Select(x => $"{x.AttributeName}: {x.NormalizedValue}"));
            result.Add(new EntityVariantGraphDto
            {
                IsMain = result.Count == 0,
                // BuildVariantCode zaten Türkçe-farkında büyütür (ı→I, i→İ; deterministik) — ayrıca ToUpperInvariant GEREKMEZ (Türkçe'yi bozardı).
                Code = EntityVariantSynchronizer.BuildVariantCode(valueNames),
                Name = EntityVariantSynchronizer.BuildVariantName(input.OwnerName?.Trim() ?? string.Empty, valueNames).Trim(),
                IsActive = true,
                AttributeSummary = summary,
                CombinationKey = BuildCombinationKeyFromClientKeys(combination.Select(x => x.Value.ClientKey)),
            });
        }

        return result;
    }

    public async Task<List<CommodityVariantOptionDto>> GetActiveVariantOptionsAsync(string entityName, Guid entityId)
    {
        // Company + tenant filtreleri kapatılır (entityId zaten TEK sahibe daraltıyor — görünürlük sızıntısı yok:
        // çağıran entityId'yi kendi görünür kümesinden almış olur).
        // NOT (görev #4 düzeltmesi): eski gerekçe "host-seviyesi emtia kataloğu (TenantId=null; ör. madenler)"
        // diyordu — bu ARTIK GEÇERSİZ; emtialar ICompanyOwned ve host'ta üretilemiyor (canlıda 0 host satırı vardı,
        // yani gerekçe hiç doğru olmamıştı). Filtre kapatma yine de DOĞRU: varyant satırları emtiadan FARKLI bir
        // company damgası taşıyabildiğinden (EntityVariant ICompanyScoped) working-context'te combo boş kalıyordu.
        using (_dataFilter.Disable<IMultiTenant>())
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            return await _asyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.EntityName == entityName && v.EntityId == entityId && v.IsActive)
                    .OrderByDescending(v => v.IsMain).ThenBy(v => v.Code)
                    .Select(v => new CommodityVariantOptionDto { Id = v.Id, Code = v.Code, IsMain = v.IsMain }));
        }
    }

    public virtual async Task<Dictionary<Guid, Guid>> GetMainVariantMapAsync(string entityName, IReadOnlyCollection<Guid> entityIds)
    {
        var result = new Dictionary<Guid, Guid>();
        if (entityIds == null || entityIds.Count == 0)
        {
            return result;
        }

        var en = (entityName ?? string.Empty).Trim();
        var ids = entityIds.Distinct().ToList();
        var rows = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == en && ids.Contains(v.EntityId) && v.IsMain)
                .Select(v => new { v.EntityId, v.Id }));
        foreach (var r in rows)
        {
            result[r.EntityId] = r.Id;   // ana varyant değişmezi: entity başına tek
        }

        return result;
    }

    public async Task DeleteForAsync(string entityName, Guid entityId, Func<IReadOnlyList<Guid>, Task>? deleteExtensionAsync = null)
    {
        var variantIds = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == entityName && v.EntityId == entityId).Select(v => v.Id));
        if (variantIds.Count > 0)
        {
            if (deleteExtensionAsync != null)
            {
                await deleteExtensionAsync(variantIds);   // sahip uzantısı (GoodVariantDetail) — varyantlar silinmeden önce
            }

            await _linkRepository.DeleteAsync(l => variantIds.Contains(l.EntityVariantId), autoSave: true);
        }

        await _variantManager.DeleteVariantsOfEntityAsync(entityName, entityId);

        var attributeIds = await _asyncExecuter.ToListAsync(
            (await _attributeRepository.GetQueryableAsync())
                .Where(a => a.EntityName == entityName && a.EntityId == entityId).Select(a => a.Id));
        if (attributeIds.Count > 0)
        {
            await _valueRepository.DeleteAsync(v => attributeIds.Contains(v.EntityAttributeId), autoSave: true);
            await _attributeRepository.DeleteAsync(a => a.EntityName == entityName && a.EntityId == entityId, autoSave: true);
        }
    }

    // ── nitelik grafı diff → değer ClientKey → persist ValueId eşlemesi ──

    private async Task<Dictionary<Guid, Guid>> SaveAttributesAsync(
        string entityName, Guid entityId, Guid? companyId, List<EntityAttributeGraphDto> attributes)
    {
        var valueIdByClientKey = new Dictionary<Guid, Guid>();
        if (attributes == null)
        {
            return valueIdByClientKey;
        }

        foreach (var a in attributes.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            await _valueRepository.DeleteAsync(v => v.EntityAttributeId == a.Id, autoSave: true);
            await _attributeRepository.DeleteAsync(a.Id, autoSave: true);
        }

        var survivors = attributes.Where(x => !x.IsDeleted).ToList();
        if (survivors.Count > EntityVariantConsts.MaxAttributesPerEntity)
        {
            throw new BusinessException("TradeXpress:EntityVariant:TooManyAttributes");
        }

        EnsureAttributeNamesUnique(survivors);
        EnsureEveryAttributeHasValue(survivors);

        foreach (var a in survivors)
        {
            if (a.Id == Guid.Empty)
            {
                var attribute = new EntityAttribute(companyId, entityName, entityId, a.Name, a.DisplayOrder);
                await _attributeRepository.InsertAsync(attribute, autoSave: true);
                a.Id = attribute.Id;
            }
            else
            {
                var attribute = await _attributeRepository.GetAsync(a.Id);
                attribute.SetName(a.Name);
                attribute.SetDisplayOrder(a.DisplayOrder);
                await _attributeRepository.UpdateAsync(attribute, autoSave: true);
            }

            await SaveAttributeValuesAsync(companyId, a, valueIdByClientKey);
        }

        return valueIdByClientKey;
    }

    private async Task SaveAttributeValuesAsync(
        Guid? companyId, EntityAttributeGraphDto attribute, Dictionary<Guid, Guid> valueIdByClientKey)
    {
        if (attribute.Values == null)
        {
            return;
        }

        foreach (var v in attribute.Values.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            await _valueRepository.DeleteAsync(v.Id, autoSave: true);
        }

        var survivors = attribute.Values.Where(x => !x.IsDeleted).ToList();
        EnsureAttributeValuesUnique(survivors);

        foreach (var v in survivors)
        {
            if (v.Id == Guid.Empty)
            {
                var value = new EntityAttributeValue(companyId, attribute.Id, v.Value, v.DisplayOrder);
                await _valueRepository.InsertAsync(value, autoSave: true);
                v.Id = value.Id;
            }
            else
            {
                var value = await _valueRepository.GetAsync(v.Id);
                value.SetValue(v.Value);
                value.SetDisplayOrder(v.DisplayOrder);
                await _valueRepository.UpdateAsync(value, autoSave: true);
            }

            valueIdByClientKey[v.ClientKey] = v.Id;
        }
    }

    private static void EnsureAttributeNamesUnique(List<EntityAttributeGraphDto> survivors)
    {
        var names = survivors.Select(a => StringFieldGuard.NormalizeName(
            a.Name, nameof(EntityAttribute.Name), EntityFieldConsts.NameMinLength, EntityVariantConsts.AttributeNameMaxLength));
        if (HasDuplicate(names))
        {
            throw new BusinessException("TradeXpress:EntityAttribute:NameAlreadyExists");
        }
    }

    private static void EnsureEveryAttributeHasValue(List<EntityAttributeGraphDto> survivors)
    {
        var hasEmpty = survivors.Any(a => a.Values == null || a.Values.All(v => v.IsDeleted));
        if (hasEmpty)
        {
            throw new BusinessException("TradeXpress:EntityAttribute:ValueRequired");
        }
    }

    // Değer CASE-KORUR ama DB unique index case-INSENSITIVE → uyarı da CI (trim + OrdinalIgnoreCase).
    private static void EnsureAttributeValuesUnique(List<EntityAttributeValueGraphDto> survivors)
    {
        if (HasDuplicate(survivors.Select(v => v.Value.Trim())))
        {
            throw new BusinessException("TradeXpress:EntityAttributeValue:ValueAlreadyExists");
        }
    }

    private static bool HasDuplicate(IEnumerable<string> normalized)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return normalized.Any(n => !seen.Add(n));
    }

    // ── varyant grafı: YALNIZ çekirdek özelleştirme (Barkod/Stok/Açıklama/Aktif); Kod/Ad otomatik; IsMain manager'da ──
    private async Task ApplyVariantCustomizationsAsync(
        string entityName, Guid entityId, IReadOnlyList<EntityVariantGraphDto> variants,
        Dictionary<Guid, Guid> valueIdByClientKey, Func<EntityVariantGraphDto, Guid, Task>? saveExtensionAsync)
    {
        if (variants == null || variants.Count == 0)
        {
            return;
        }

        var dbVariants = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync()).Where(v => v.EntityName == entityName && v.EntityId == entityId));
        var variantIds = dbVariants.Select(v => v.Id).ToList();
        var links = variantIds.Count == 0
            ? new List<EntityVariantAttributeValue>()
            : await _asyncExecuter.ToListAsync(
                (await _linkRepository.GetQueryableAsync()).Where(l => variantIds.Contains(l.EntityVariantId)));

        var byCombination = dbVariants.ToDictionary(
            v => EntityVariantSynchronizer.BuildKey(
                links.Where(l => l.EntityVariantId == v.Id).Select(l => l.EntityAttributeValueId)),
            v => v);

        foreach (var v in variants)
        {
            var target = ResolveTargetVariant(v);
            if (target == null)
            {
                continue;
            }

            target.SetActive(v.IsActive);
            target.SetDescription(v.Description);
            target.SetBarcode(v.Barcode);
            target.SetTradeIdentifiers(v.Gtin, v.Mpn, v.Oem);
            target.SetStock(v.StockQuantity);
            await _variantRepository.UpdateAsync(target, autoSave: true);

            // Sahip entity-özel uzantı (ör. Good fiyat/stok → GoodVariantDetail) — çözülen DB varyanta bağla.
            if (saveExtensionAsync != null)
            {
                await saveExtensionAsync(v, target.Id);
            }
        }

        EntityVariant? ResolveTargetVariant(EntityVariantGraphDto dto)
        {
            if (dto.Id != Guid.Empty)
            {
                return dbVariants.FirstOrDefault(x => x.Id == dto.Id);
            }

            if (dto.IsMain && string.IsNullOrEmpty(dto.CombinationKey))
            {
                return dbVariants.FirstOrDefault(x => x.IsMain);
            }

            if (string.IsNullOrEmpty(dto.CombinationKey))
            {
                return null;
            }

            var valueIds = new List<Guid>();
            foreach (var part in dto.CombinationKey.Split('|'))
            {
                if (!Guid.TryParse(part, out var clientKey) || !valueIdByClientKey.TryGetValue(clientKey, out var valueId))
                {
                    return null;
                }

                valueIds.Add(valueId);
            }

            return byCombination.GetValueOrDefault(EntityVariantSynchronizer.BuildKey(valueIds));
        }
    }

    // ── persistsiz üretim (GenerateVariants) yardımcıları ──

    private static List<List<GenerationAxisItem>> BuildGenerationAxes(List<EntityAttributeGraphDto> attributes)
    {
        var survivors = (attributes ?? new List<EntityAttributeGraphDto>())
            .Where(a => !a.IsDeleted)
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
            .ToList();

        var axes = new List<List<GenerationAxisItem>>();
        foreach (var attribute in survivors)
        {
            var attributeName = StringFieldGuard.NormalizeName(
                attribute.Name, nameof(EntityAttribute.Name), EntityFieldConsts.NameMinLength, EntityVariantConsts.AttributeNameMaxLength);

            var values = (attribute.Values ?? new List<EntityAttributeValueGraphDto>())
                .Where(v => !v.IsDeleted)
                .Select(v => new GenerationAxisItem(v, v.Value.Trim(), attributeName))
                .OrderBy(x => x.Value.DisplayOrder).ThenBy(x => x.NormalizedValue)
                .ToList();

            if (values.Count == 0)
            {
                throw new BusinessException("TradeXpress:EntityAttribute:ValueRequired");
            }

            axes.Add(values);
        }

        return axes;
    }

    private sealed record GenerationAxisItem(EntityAttributeValueGraphDto Value, string NormalizedValue, string AttributeName);

    private static string BuildCombinationKeyFromClientKeys(IEnumerable<Guid> clientKeys)
    {
        return string.Join("|", clientKeys.OrderBy(k => k));
    }

    // Yüklenen varyantın CombinationKey'i — bağlarındaki değer DB Id'lerini YÜKLÜ değer DTO'larının ClientKey'lerine map'ler
    // (GenerateVariants ile AYNI format: sıralı "|" join). Yükleme sonrası otomatik regen MERGE'i bununla eşler → düzenleme korunur.
    // Bağı olmayan (base main) → boş (GenerateVariants ana-kombinasyonuyla tutarlı; save Id ile eşlediğinden yan etki yok).
    private static string BuildLoadedCombinationKey(
        Guid variantId,
        List<EntityVariantAttributeValue> links,
        Dictionary<Guid, EntityAttributeValueGraphDto> valueDtoByDbId)
    {
        var clientKeys = links
            .Where(l => l.EntityVariantId == variantId && valueDtoByDbId.ContainsKey(l.EntityAttributeValueId))
            .Select(l => valueDtoByDbId[l.EntityAttributeValueId].ClientKey)
            .ToList();
        return clientKeys.Count == 0 ? string.Empty : BuildCombinationKeyFromClientKeys(clientKeys);
    }

    private static string BuildAttributeSummary(
        Guid variantId,
        List<EntityAttribute> attributes,
        List<EntityAttributeValue> values,
        List<EntityVariantAttributeValue> links)
    {
        var valueById = values.ToDictionary(v => v.Id);
        var attributeById = attributes.ToDictionary(a => a.Id);
        var attributeOrder = attributes
            .Select((a, index) => (a.Id, Index: index))
            .ToDictionary(x => x.Id, x => x.Index);

        var parts = links
            .Where(l => l.EntityVariantId == variantId
                && valueById.ContainsKey(l.EntityAttributeValueId)
                && attributeById.ContainsKey(l.EntityAttributeId))
            .OrderBy(l => attributeOrder.GetValueOrDefault(l.EntityAttributeId, int.MaxValue))
            .Select(l => $"{attributeById[l.EntityAttributeId].Name}: {valueById[l.EntityAttributeValueId].Value}");

        return string.Join(", ", parts);
    }
}
