using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels.Variants;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// Agnostik nitelik-driven varyant ÜRETİM/SENKRON servisi — SUNUCU tarafında. Sahip entity'nin (EntityName+EntityId)
/// nitelik × değer KARTEZYENİNİ mevcut varyant setiyle mutabık kılar (0 nitelik → tek base varyant; yeni kombinasyon →
/// yeni varyant + bağlar; geçersiz kombinasyon → sil; sonda tekil-main). Herhangi bir entity (Good, Product, Metal…)
/// aynı synchronizer'ı kullanır. Kartezyen matematiği paylaşılan <see cref="VariantCombinationEngine"/>'den.
/// Company görünürlük filtresi kapatılır (sorgular EntityName+EntityId ile daraltılmış → sızıntı yok).
/// </summary>
public class EntityVariantSynchronizer : DomainService
{
    private readonly IRepository<EntityAttribute, Guid> _attributeRepository;
    private readonly IRepository<EntityAttributeValue, Guid> _valueRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<EntityVariantAttributeValue, Guid> _linkRepository;
    private readonly EntityVariantManager _variantManager;
    private readonly IDataFilter _dataFilter;

    public EntityVariantSynchronizer(
        IRepository<EntityAttribute, Guid> attributeRepository,
        IRepository<EntityAttributeValue, Guid> valueRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<EntityVariantAttributeValue, Guid> linkRepository,
        EntityVariantManager variantManager,
        IDataFilter dataFilter)
    {
        _attributeRepository = attributeRepository;
        _valueRepository = valueRepository;
        _variantRepository = variantRepository;
        _linkRepository = linkRepository;
        _variantManager = variantManager;
        _dataFilter = dataFilter;
    }

    /// <summary>Sahip entity'nin varyant setini nitelik/değer tanımıyla mutabık kılar (idempotent). <paramref name="ownerName"/>
    /// varyant AD türetmesi içindir (ör. Good.Name); <paramref name="ownerCode"/> NİTELİKSİZ tek varyantın kod kimliğidir.</summary>
    public async Task SynchronizeAsync(
        string entityName, Guid entityId, Guid? companyId, string ownerName, string? ownerCode = null)
    {
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            var attributes = (await AsyncExecuter.ToListAsync(
                    (await _attributeRepository.GetQueryableAsync())
                        .Where(a => a.EntityName == entityName && a.EntityId == entityId)))
                .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
                .ToList();

            // 0 nitelik → tek base varyant; nitelikli dönemden kalan bağlı varyantlar temizlenir.
            if (attributes.Count == 0)
            {
                await RemoveLinkedVariantsAsync(entityName, entityId);
                var main = await _variantManager.EnsureMainVariantAsync(
                    entityName, entityId, companyId, ownerCode, ownerName);

                // TEK VARYANT SAHİBİ İZLER (2026-08-06 Hakan kararı: "ANAVARYANT boşa çıkmalı"): niteliksiz kayıtta
                // varyant kimliği görünmez ve otomatiktir (form ShowIdentity=false gizliyor) → kod/ad her kayıtta
                // sahibe eşitlenir. Böylece hem eski "ANAVARYANT"lı kayıtlar İLK kayıtta kendini onarır hem sahibin
                // kod/ad değişikliği varyanta yansır (bayat kod pazaryerine SKU olarak gitmesin). Nitelikli
                // (kombinasyon) varyantlara DOKUNULMAZ — kodları değer adlarından türer.
                if (!string.IsNullOrWhiteSpace(ownerCode) && ApplyOwnerIdentity(main, ownerCode, ownerName))
                {
                    await _variantRepository.UpdateAsync(main, autoSave: true);
                }

                return;
            }

            var attributeIds = attributes.Select(a => a.Id).ToList();
            var values = (await AsyncExecuter.ToListAsync(
                    (await _valueRepository.GetQueryableAsync()).Where(v => attributeIds.Contains(v.EntityAttributeId))))
                .OrderBy(v => v.DisplayOrder).ThenBy(v => v.Value)
                .ToList();

            var axes = attributes
                .Select(a => (Attribute: a, Values: values.Where(v => v.EntityAttributeId == a.Id).ToList()))
                .ToList();

            // Değersiz nitelik varken kartezyen BOŞTUR → mevcut seti koru, yalnız main'i garanti et.
            // ownerCode/Name BURADA DA GEÇİLİR: geçilmezse yeni doğan ana varyant "ANAVARYANT" sentinel kodunu
            // alır ve o kod pazaryerine SKU olarak gider (2026-08-06 kararının önlemek istediği şey). Sentinel
            // yalnız sahip kimliğinin GERÇEKTEN bilinmediği savunma yolunda kalmalı — burada biliniyor.
            if (axes.Any(x => x.Values.Count == 0))
            {
                await _variantManager.EnsureMainVariantAsync(entityName, entityId, companyId, ownerCode, ownerName);
                return;
            }

            var target = BuildCartesian(axes);

            var existingVariants = await AsyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.EntityName == entityName && v.EntityId == entityId));
            var variantIds = existingVariants.Select(v => v.Id).ToList();
            var links = await AsyncExecuter.ToListAsync(
                (await _linkRepository.GetQueryableAsync()).Where(l => variantIds.Contains(l.EntityVariantId)));

            var existingByKey = existingVariants.ToDictionary(
                v => BuildKey(links.Where(l => l.EntityVariantId == v.Id).Select(l => l.EntityAttributeValueId)),
                v => v);

            // 1) Hedefte OLMAYAN mevcutlar (bağ'sız base'ler + kaldırılan kombinasyonlar) → sil.
            var targetKeys = target.Select(c => BuildKey(c.Select(x => x.Value.Id))).ToHashSet();
            foreach (var (key, variant) in existingByKey)
            {
                if (!targetKeys.Contains(key))
                {
                    await _linkRepository.DeleteAsync(l => l.EntityVariantId == variant.Id, autoSave: true);
                    await _variantRepository.DeleteAsync(variant, autoSave: true);
                }
            }

            // 2) Hedefte olup mevcutta OLMAYAN kombinasyonlar → yeni varyant (AKTİF; kod/ad otomatik türer).
            foreach (var combination in target)
            {
                var key = BuildKey(combination.Select(x => x.Value.Id));
                if (existingByKey.ContainsKey(key))
                {
                    continue;
                }

                var variant = new EntityVariant(
                    companyId,
                    entityName,
                    entityId,
                    BuildVariantCode(combination.Select(x => x.Value.Value)),
                    BuildVariantName(ownerName, combination.Select(x => x.Value.Value)));
                await _variantRepository.InsertAsync(variant, autoSave: true);

                foreach (var (attribute, value) in combination)
                {
                    await _linkRepository.InsertAsync(
                        new EntityVariantAttributeValue(companyId, variant.Id, attribute.Id, value.Id),
                        autoSave: true);
                }
            }

            // Kombinasyon varyantlarının kodu değer adlarından türer; yine de ownerCode geçilir ki HİÇ varyant
            // üretilememiş uç durumda doğacak ana varyant sentinel kod almasın.
            await _variantManager.EnsureMainVariantAsync(entityName, entityId, companyId, ownerCode, ownerName);
        }
    }

    /// <summary>Niteliksiz tek varyantın kod/adını sahibe eşitler; değişiklik olduysa true (gereksiz UPDATE yok).</summary>
    private static bool ApplyOwnerIdentity(EntityVariant main, string ownerCode, string? ownerName)
    {
        var changed = false;
        if (!string.Equals(main.Code, ownerCode, StringComparison.Ordinal))
        {
            main.SetCode(ownerCode);
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(ownerName) && !string.Equals(main.Name, ownerName, StringComparison.Ordinal))
        {
            main.SetName(ownerName);
            changed = true;
        }

        return changed;
    }

    /// <summary>Kombinasyon imzası — sıralı valueId dizisi. PUBLIC: AppService/servis kayıt-öncesi üretilen (Id'siz)
    /// varyantı DB kombinasyonuyla eşlerken AYNI imzayı kullanır (DRY). Format <c>VariantCombinationEngine</c>'dedir.</summary>
    public static string BuildKey(IEnumerable<Guid> valueIds)
    {
        return VariantCombinationEngine.BuildKey(valueIds);
    }

    /// <summary>Varyant kodu değer adlarından OTOMATİK türer ("KIRMIZI-42"); üst sınıra sığdırılır.
    /// PUBLIC: persistsiz üretim önizlemesi (GenerateVariants) AYNI türetmeyi kullanır (DRY).
    /// <para>BÜYÜTME Türkçe-farkındadır (<see cref="ToTurkishUpper"/>): kod Türkçe nitelik değerlerinden türer +
    /// kullanıcıya SKU olarak görünür → "Kırmızı"→"KIRMIZI", "Yeşil"→"YEŞİL". Invariant büyütme bunları "KıRMıZı"/"YEŞIL"
    /// (bozuk) yapardı. Dönüşüm DETERMİNİSTİK (thread kültüründen bağımsız) → konvansiyonun çatalsız-benzersizlik amacı korunur.</para></summary>
    public static string BuildVariantCode(IEnumerable<string> valueNames)
    {
        var joined = ToTurkishUpper(string.Join("-", valueNames));
        return joined.Length <= EntityVariantConsts.VariantCodeMaxLength
            ? joined
            : joined[..EntityVariantConsts.VariantCodeMaxLength];
    }

    // Türkçe büyütme: ı→I (noktasız) ve i→İ (noktalı) ÖNCE map'lenir, sonra ToUpperInvariant kalanları (ş/ç/ğ/ö/ü + ASCII)
    // büyütür. Sonuç ToUpperInvariant altında KARARLIDIR (İ→İ, I→I) → kayıttaki NormalizeAsCode (SetCode) kodu geri bozmaz.
    // 'ı' = ı (dotless small), 'İ' = İ (dotted capital); 'I'/'i' ASCII. Deterministik (CurrentCulture kullanmaz).
    private static string ToTurkishUpper(string value)
    {
        return value.Replace('ı', 'I').Replace('i', 'İ').ToUpperInvariant();
    }

    /// <summary>PUBLIC: üretim önizlemesiyle ad türetme paritesi — bkz. <see cref="BuildVariantCode"/>.</summary>
    public static string BuildVariantName(string ownerName, IEnumerable<string> valueNames)
    {
        var joined = $"{ownerName} {string.Join(" ", valueNames)}";
        return joined.Length <= EntityVariantConsts.VariantNameMaxLength
            ? joined
            : joined[..EntityVariantConsts.VariantNameMaxLength];
    }

    // Kartezyen: her nitelikten (axis) bir değer. Matematik VariantCombinationEngine'dedir.
    private static List<List<(EntityAttribute Attribute, EntityAttributeValue Value)>> BuildCartesian(
        List<(EntityAttribute Attribute, List<EntityAttributeValue> Values)> axes)
    {
        return VariantCombinationEngine.BuildCartesian(
            axes.Select(a => (a.Attribute, (IReadOnlyList<EntityAttributeValue>)a.Values)).ToList());
    }

    // Nitelik-bağlı TÜM varyantları (bağlarıyla) siler — nitelikler tamamen kaldırılınca.
    private async Task RemoveLinkedVariantsAsync(string entityName, Guid entityId)
    {
        var variantIds = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == entityName && v.EntityId == entityId).Select(v => v.Id));
        if (variantIds.Count == 0)
        {
            return;
        }

        var linkedVariantIds = (await AsyncExecuter.ToListAsync(
                (await _linkRepository.GetQueryableAsync())
                    .Where(l => variantIds.Contains(l.EntityVariantId))
                    .Select(l => l.EntityVariantId)))
            .Distinct()
            .ToList();

        foreach (var id in linkedVariantIds)
        {
            await _linkRepository.DeleteAsync(l => l.EntityVariantId == id, autoSave: true);
            await _variantRepository.DeleteAsync(v => v.Id == id, autoSave: true);
        }
    }
}
