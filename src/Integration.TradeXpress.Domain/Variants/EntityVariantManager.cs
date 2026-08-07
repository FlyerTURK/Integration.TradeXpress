using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// Sahip entity → varyant değişmezlerini tek noktada toplar: her sahip (EntityName+EntityId) en az bir varyantla +
/// <b>tekil main</b> varyantla yaşar. Agnostik — herhangi bir entity (Good, Product, Metal…) aynı manager'ı kullanır.
/// Company görünürlük filtresi kapatılır (sorgu EntityName+EntityId ile daraltıldığı için sızıntı yok).
/// </summary>
public class EntityVariantManager : DomainService
{
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IDataFilter _dataFilter;

    public EntityVariantManager(
        IRepository<EntityVariant, Guid> variantRepository,
        IDataFilter dataFilter)
    {
        _variantRepository = variantRepository;
        _dataFilter = dataFilter;
    }

    /// <summary>Sahip entity'nin ana (main) varyantını garanti eder (idempotent): main varsa döner (fazlalıkları
    /// düşürür); varyant var main yoksa ilkini (en düşük Code) main yapar; hiç yoksa varsayılan main kurar.
    ///
    /// <para><b>Varsayılan main'in kimliği SAHİPTEN gelir</b> (2026-08-06 Hakan kararı: <i>"tek bir varyant demek,
    /// ayrıma gitmemek demek — ANAVARYANT boşa çıkmalı"</i>): <paramref name="ownerCode"/>/<paramref name="ownerName"/>
    /// verilirse yeni kurulan varyant sahibin kodunu/adını taşır; "ANAVARYANT" sentinel'i yalnız sahibin kimliği
    /// bilinmediğinde savunma olarak kalır. Push zinciri varyant kodunu SKU olarak gönderdiğinden bu doğrudan
    /// pazaryerine yansır — "1234" gider, "ANAVARYANT" değil.</para></summary>
    public async Task<EntityVariant> EnsureMainVariantAsync(
        string entityName, Guid entityId, Guid? companyId, string? ownerCode = null, string? ownerName = null)
    {
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            var variants = (await AsyncExecuter.ToListAsync(
                    (await _variantRepository.GetQueryableAsync())
                        .Where(v => v.EntityName == entityName && v.EntityId == entityId)))
                .OrderBy(v => v.Code)
                .ToList();

            var main = variants.FirstOrDefault(v => v.IsMain);
            if (main != null)
            {
                await UnsetOtherMainsAsync(entityName, entityId, main.Id);
                return main;
            }

            if (variants.Count > 0)
            {
                var promote = variants.First();
                promote.SetAsMain(true);
                await _variantRepository.UpdateAsync(promote, autoSave: true);
                await UnsetOtherMainsAsync(entityName, entityId, promote.Id);
                return promote;
            }

            var variant = new EntityVariant(
                companyId,
                entityName,
                entityId,
                string.IsNullOrWhiteSpace(ownerCode) ? EntityVariantConsts.MainVariantCode : ownerCode,
                string.IsNullOrWhiteSpace(ownerName) ? EntityVariantConsts.MainVariantName : ownerName,
                isMain: true);

            await _variantRepository.InsertAsync(variant, autoSave: true);
            return variant;
        }
    }

    /// <summary>Verilen varyantı ANA yapar ve sahip başına tekil-ana değişmezini korur (diğerlerinin bayrağı
    /// düşürülür). Muadil materyalizasyonu Rank 1 kombinasyonu ana yaparken kullanır (ADR-PRODUCT-ORCHESTRATION).</summary>
    public async Task SetMainVariantAsync(EntityVariant variant)
    {
        if (!variant.IsMain)
        {
            variant.SetAsMain(true);
            await _variantRepository.UpdateAsync(variant, autoSave: true);
        }

        await UnsetOtherMainsAsync(variant.EntityName, variant.EntityId, variant.Id);
    }

    /// <summary>Sahip entity'nin tüm varyantlarını siler (sahip entity silinmeden önce çağrılır).</summary>
    public async Task DeleteVariantsOfEntityAsync(string entityName, Guid entityId, bool autoSave = true)
    {
        await _variantRepository.DeleteAsync(
            v => v.EntityName == entityName && v.EntityId == entityId, autoSave: autoSave);
    }

    // Sahip başına tek main: verilen hariç diğerlerinin main bayrağını düşürür.
    private async Task UnsetOtherMainsAsync(string entityName, Guid entityId, Guid exceptVariantId)
    {
        var others = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == entityName && v.EntityId == entityId && v.IsMain && v.Id != exceptVariantId));

        foreach (var o in others)
        {
            o.SetAsMain(false);
            await _variantRepository.UpdateAsync(o, autoSave: true);
        }
    }
}
