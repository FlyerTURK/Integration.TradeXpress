using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil grubu CRUD — <b>per-tenant + company-owned</b> (ICompanyOwned query-filter listeyi/get'i
/// çalışılan şirkete otomatik scope'lar; yabancı şirketin kaydı görünmez → EntityNotFound). Emtia satırları
/// grafı Create/Update input'unun İÇİNDE gelir (Account→SubAccount drill deseni) ve burada reconcile edilir:
/// Id boş → ekle, IsDeleted → sil, aksi → güncelle; <c>DisplayOrder</c> (tüketim önceliği) KORUNUR.
/// Emtia grafı yazılmadan önce <b>yazma-sınırı doğrulaması</b> yapılır (bkz. ValidateItemsAsync:
/// bayat satır · MetalId varlık/görünürlük/uygunluk · duplike maden).
/// Kod benzersizliği ŞİRKET scope'unda ön-kontrollü ((TenantId, CompanyId, Code) unique index'iyle hizalı).
/// </summary>
[Authorize(TradeXpressPermissions.Substitutions.Default)]
public class SubstitutionGroupAppService : TradeXpressAppService, ISubstitutionGroupAppService
{
    private const string MetalEntityName = "Metal";   // EntityVariant sahip-tip anahtarı (MetalAppService ile aynı)

    private readonly IRepository<SubstitutionGroup, Guid> _repository;
    private readonly IRepository<SubstitutionGroupItem, Guid> _itemRepository;
    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<Product, Guid> _productRepository;   // yalnız OKUMA — silme "kullanımda" guard'ı
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentCompany _currentCompany;   // güvenlik sınırı: working-context zorlaması

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id", "ToleranceType", "ToleranceValue" };

    public SubstitutionGroupAppService(
        IRepository<SubstitutionGroup, Guid> repository,
        IRepository<SubstitutionGroupItem, Guid> itemRepository,
        IRepository<Metal, Guid> metalRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<Product, Guid> productRepository,
        IDataFilter dataFilter,
        ICurrentCompany currentCompany)
    {
        _productRepository = productRepository;
        _repository        = repository;
        _itemRepository    = itemRepository;
        _metalRepository   = metalRepository;
        _variantRepository = variantRepository;
        _dataFilter        = dataFilter;
        _currentCompany    = currentCompany;
    }

    public virtual async Task<PagedResultDto<SubstitutionGroupListDto>> GetListAsync(SubstitutionGroupListRequestDto input)
    {
        // Varsayılan sıralama: Code artan (kullanıcı sıralaması yoksa — Stone/Metal fallback'iyle hizalı).
        if (input.Sorts is not { Count: > 0 } && string.IsNullOrWhiteSpace(input.Sorting))
        {
            input.Sorts = new List<SortField> { new() { Field = "Code" } };
        }

        // Company scope'u query-filter verir (client CompanyId göndermez — grid standardı).
        var query = (await _repository.GetQueryableAsync())
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.ApplyPaging(input));

        return new PagedResultDto<SubstitutionGroupListDto>(
            totalCount,
            items.Select(ObjectMapper.Map<SubstitutionGroup, SubstitutionGroupListDto>).ToList());
    }

    public virtual async Task<SubstitutionGroupGetDto> GetAsync(Guid id)
    {
        return await ToGetDtoAsync(await _repository.GetAsync(id));
    }

    [Authorize(TradeXpressPermissions.Substitutions.Create)]
    public virtual async Task<SubstitutionGroupGetDto> CreateAsync(SubstitutionGroupCreateDto input)
    {
        var companyId = EnsureCurrentCompanyId();

        // Benzersizlik ÖN-kontrolü (Create+Update simetrik): normalize → şirket scope'unda dostane hata.
        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(SubstitutionGroup.Code), EntityFieldConsts.CodeMinLength, SubstitutionGroupConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(companyId, normalizedCode, Guid.Empty);

        var entity = new SubstitutionGroup(companyId, input.Code, input.Name, input.Type);
        entity.SetTolerance(input.ToleranceType, input.ToleranceValue);
        entity.SetDescription(input.Description);

        await _repository.InsertAsync(entity, autoSave: true);
        await SaveItemsAsync(entity, input.Items);
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.Substitutions.Update)]
    public virtual async Task<SubstitutionGroupGetDto> UpdateAsync(Guid id, SubstitutionGroupUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);

        await ApplyCodeChangeAsync(entity, input.Code);
        entity.SetName(input.Name);
        entity.SetType(input.Type);
        entity.SetTolerance(input.ToleranceType, input.ToleranceValue);
        entity.SetDescription(input.Description);
        entity.SetActive(input.IsActive);

        await _repository.UpdateAsync(entity, autoSave: true);
        await SaveItemsAsync(entity, input.Items);
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.Substitutions.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        // Güvenlik sınırı (Account deseni): grubu ÖNCE yükle — company query-filter yabancı şirketin
        // kaydını gizler → EntityNotFound; satır silme ancak doğrulamadan SONRA yapılır.
        var entity = await _repository.GetAsync(id);

        await EnsureNotInUseAsync(entity.Id);
        await _itemRepository.DeleteAsync(i => i.SubstitutionGroupId == entity.Id, autoSave: true);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    /// <summary>Silme "kullanımda" guard'ı (ShipmentTemplate/AddOn deseni — kod-inceleme bulgusu). Aggregate'ler arası
    /// referans id-only olduğundan DB'de FK kısıtı yoktur: guard olmadan grup silinince <see cref="Product"/>'ın
    /// <c>SubstitutionGroupId</c>'si dangling kalıyor, ürün formu hata toast'ı + boş override ağacı gösteriyor ve
    /// "Kombinasyon Hesapla" GroupNotFound veriyordu. EF eşlemesindeki AppProducts.SubstitutionGroupId indeksi zaten
    /// bu sorgu için konmuştu.</summary>
    private async Task EnsureNotInUseAsync(Guid groupId)
    {
        if (await _productRepository.AnyAsync(p => p.SubstitutionGroupId == groupId))
        {
            throw new BusinessException("TradeXpress:Substitution:GroupInUse");
        }
    }

    /// <summary>Kod değişikliği (ürün kuralı 2026-07-04): normalize → değiştiyse AYNI ŞİRKET scope'unda
    /// benzersizliği doğrula (kendisi hariç) → uygula. Dostane hata, ham DB unique çakışması değil.</summary>
    private async Task ApplyCodeChangeAsync(SubstitutionGroup entity, string rawCode)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            rawCode, nameof(entity.Code), EntityFieldConsts.CodeMinLength, SubstitutionGroupConsts.CodeMaxLength);
        if (string.Equals(normalizedCode, entity.Code, StringComparison.Ordinal))
        {
            return; // değişmedi
        }

        await EnsureCodeUniqueAsync(entity.CompanyId, normalizedCode, entity.Id);
        entity.SetCode(normalizedCode);
    }

    /// <summary>Aynı ŞİRKET altında Code benzersizliği ((TenantId,CompanyId,Code) unique index'iyle hizalı).
    /// Create'te <paramref name="excludeId"/>=Guid.Empty, Update'te entity.Id (kendisi hariç).</summary>
    private async Task EnsureCodeUniqueAsync(Guid companyId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(g => g.CompanyId == companyId && g.Id != excludeId && g.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:Substitution:CodeAlreadyExists");
        }
    }

    // ── emtia satırı grafı reconcile (Id + IsDeleted diff; Account→SubAccount deseniyle aynı akış) ──
    private async Task SaveItemsAsync(SubstitutionGroup group, List<SubstitutionGroupItemGraphDto> items)
    {
        if (items == null)
        {
            return;
        }

        var existingById = (await _itemRepository.GetListAsync(i => i.SubstitutionGroupId == group.Id))
            .ToDictionary(i => i.Id);

        // Yazma sınırı fail-fast: bayat satır + MetalId varlık/uygunluk + duplike — mutasyondan ÖNCE.
        await ValidateItemsAsync(group, items, existingById);

        // Dahil varyant kümeleri: aidiyet doğrulaması + "{yalnız ana} → boş" normalizasyonu (tek temsil).
        await NormalizeIncludedVariantsAsync(items);

        // Önce ekle + güncelle, sonra sil (Branch→Vault deseniyle aynı sıra).
        foreach (var node in items.Where(x => !x.IsDeleted))
        {
            if (node.Id == Guid.Empty)
            {
                // Boş hedef (MetalId=null) kontrolü entity'de (SetTarget fail-fast: ItemTargetRequired);
                // varlık/uygunluk ValidateItemsAsync'te doğrulandı.
                var created = new SubstitutionGroupItem(group.CompanyId, group.Id, node.MetalId, null, node.DisplayOrder);
                created.SetIncludedVariants(node.IncludedVariantIds);
                await _itemRepository.InsertAsync(created, autoSave: true);
            }
            else
            {
                // Varlık ValidateItemsAsync'te garanti (bayat Id → ItemStale) — sessiz atlama yok.
                var existing = existingById[node.Id];
                existing.SetTarget(node.MetalId, null);
                existing.SetDisplayOrder(node.DisplayOrder);
                existing.SetIncludedVariants(node.IncludedVariantIds);
                await _itemRepository.UpdateAsync(existing, autoSave: true);
            }
        }

        foreach (var node in items.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            if (existingById.ContainsKey(node.Id))
            {
                await _itemRepository.DeleteAsync(node.Id, autoSave: true);
            }
        }
    }

    /// <summary>Emtia grafının yazma-sınırı doğrulaması — solver kuralı KAYNAKTA zorlanır (client'taki
    /// picker filtresi güven sınırı DEĞİLDİR, ham API çağrısı da aynı kurallardan geçer):
    /// <list type="number">
    ///   <item><b>Bayat satır:</b> dolu-Id'li güncelleme node'u DB'de yoksa (başka oturum silmiş olabilir)
    ///   sessiz veri kaybı yerine dostane hata.</item>
    ///   <item><b>Varlık + görünürlük:</b> MetalId host (TenantId=null) ya da kendi tenant'ının kataloğunda
    ///   olmalı — yabancı tenant'ın metali persist edilemez / kodu ifşa edilemez.</item>
    ///   <item><b>Uygunluk:</b> yalnız adet-hesaplı + standart gramajlı (IsQuantity + StableQuantity&gt;0)
    ///   maden muadil olabilir — hesaplama anında değil, kayıt anında fail-fast.</item>
    ///   <item><b>Duplike:</b> grubun SON hâlinde aynı maden iki kez yer alamaz (hesaplama servisindeki
    ///   ToDictionary çökmesi kaynağında önlenir).</item>
    /// </list></summary>
    private async Task ValidateItemsAsync(
        SubstitutionGroup group,
        List<SubstitutionGroupItemGraphDto> items,
        Dictionary<Guid, SubstitutionGroupItem> existingById)
    {
        var liveNodes = items.Where(x => !x.IsDeleted).ToList();

        if (liveNodes.Any(x => x.Id != Guid.Empty && !existingById.ContainsKey(x.Id)))
        {
            throw new BusinessException("TradeXpress:Substitution:ItemStale");
        }

        // Grubun SON hâli projeksiyonu: silinmeyen mevcutlar (güncelleme uygulanmış) + yeni eklenenler.
        var deletedIds = items.Where(x => x.IsDeleted && x.Id != Guid.Empty).Select(x => x.Id).ToHashSet();
        var updatedById = new Dictionary<Guid, SubstitutionGroupItemGraphDto>();
        foreach (var node in liveNodes.Where(x => x.Id != Guid.Empty))
        {
            updatedById[node.Id] = node; // aynı Id iki kez gelirse son kazanır (degenerate input, çökme yok)
        }

        var finalMetalIds = new List<Guid>();
        foreach (var existing in existingById.Values.Where(e => !deletedIds.Contains(e.Id)))
        {
            var metalId = updatedById.TryGetValue(existing.Id, out var updated) ? updated.MetalId : existing.MetalId;
            if (metalId is { } value)
            {
                finalMetalIds.Add(value);
            }
        }

        finalMetalIds.AddRange(liveNodes
            .Where(x => x.Id == Guid.Empty && x.MetalId != null)
            .Select(x => x.MetalId!.Value));

        // Gelen (yeni/güncellenen) MetalId'ler varlık+uygunluk doğrulamasından geçer; duplike mesajındaki
        // kod gösterimi için SON hâldeki id'ler de aynı TEK sorguya dahil edilir.
        var incomingMetalIds = liveNodes
            .Where(x => x.MetalId != null)
            .Select(x => x.MetalId!.Value)
            .ToList();
        var metalById = await LoadVisibleMetalsAsync(incomingMetalIds.Concat(finalMetalIds));

        foreach (var metalId in incomingMetalIds.Distinct())
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
        }

        var duplicate = finalMetalIds.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            throw new BusinessException("TradeXpress:Substitution:DuplicateMetal")
                .WithData("MetalCode", metalById.TryGetValue(duplicate.Key, out var m) ? m.Code : duplicate.Key.ToString());
        }
    }

    /// <summary>Dahil varyant kümelerinin yazma-sınırı işlemesi (client'taki ağaç güven sınırı DEĞİLDİR):
    /// <list type="number">
    ///   <item><b>Aidiyet:</b> her id o SATIRIN madeninin bir varyantı olmalı — yabancı/başka madenin varyantı
    ///   persist edilemez (VariantNotOfMetal fail-fast).</item>
    ///   <item><b>Normalizasyon (tek temsil):</b> küme "{yalnız ana varyant}" ise BOŞ listeye indirgenir —
    ///   "boş = yalnız ana" değişmezinin depoda tek temsili garanti edilir (ham API çağrısı dahil).</item>
    /// </list>
    /// Varyant kataloğu HOST-seviyesi olabilir → IMultiTenant + ICompanyScoped filtreleri kapatılır ama scope
    /// AÇIKÇA daraltılır (SubstitutionCalculationAppService işçilik sorgusu deseni; sızıntı yok).</summary>
    private async Task NormalizeIncludedVariantsAsync(List<SubstitutionGroupItemGraphDto> items)
    {
        var nodes = items
            .Where(x => !x.IsDeleted && x.MetalId != null && x.IncludedVariantIds is { Count: > 0 })
            .ToList();
        if (nodes.Count == 0)
        {
            return; // boş kümeler zaten statüko (yalnız ana varyant) — sorguya gerek yok
        }

        var metalIds = nodes.Select(x => x.MetalId!.Value).Distinct().ToList();

        List<EntityVariant> variants;
        using (_dataFilter.Disable<IMultiTenant>())
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            variants = await AsyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(CompanyScopedQueryable.CompanyVisiblePredicate<EntityVariant>(CurrentTenant.Id, _currentCompany.Id))
                    .Where(v => v.EntityName == MetalEntityName && metalIds.Contains(v.EntityId)));
        }

        var variantsByMetal = variants
            .GroupBy(v => v.EntityId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var node in nodes)
        {
            var known = variantsByMetal.GetValueOrDefault(node.MetalId!.Value) ?? new List<EntityVariant>();
            var normalized = node.IncludedVariantIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (normalized.Any(id => known.All(v => v.Id != id)))
            {
                throw new BusinessException("TradeXpress:Substitution:VariantNotOfMetal");
            }

            var mainVariantId = known.FirstOrDefault(v => v.IsMain)?.Id;
            var onlyMainSelected = normalized.Count == 1 && mainVariantId == normalized[0];
            node.IncludedVariantIds = onlyMainSelected ? new List<Guid>() : normalized;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Sızıntı önleme (Account/Voucher deseni, fail-closed): aktif şirket working-context'ten
    /// zorlanır; yoksa yazma yapılamaz.</summary>
    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:Substitution:CompanyContextRequired");
        }

        return companyId;
    }

    private async Task<SubstitutionGroupGetDto> ToGetDtoAsync(SubstitutionGroup group)
    {
        var dto = ObjectMapper.Map<SubstitutionGroup, SubstitutionGroupGetDto>(group);

        var items = await _itemRepository.GetListAsync(i => i.SubstitutionGroupId == group.Id);
        var ordered = items.OrderBy(i => i.DisplayOrder).ThenBy(i => i.CreationTime).ToList();
        var metalCodes = await LoadMetalCodesAsync(ordered.Where(i => i.MetalId != null).Select(i => i.MetalId!.Value));

        dto.Items = ordered.Select(i => new SubstitutionGroupItemGraphDto
        {
            Id                 = i.Id,
            MetalId            = i.MetalId,
            MetalCode          = i.MetalId is { } metalId ? metalCodes.GetValueOrDefault(metalId, string.Empty) : string.Empty,
            DisplayOrder       = i.DisplayOrder,
            IncludedVariantIds = i.IncludedVariantIds.ToList(),
        }).ToList();

        return dto;
    }

    /// <summary>Maden kodu zenginleştirmesi (drill grid gösterimi) — görünürlük scope'u
    /// <see cref="LoadVisibleMetalsAsync"/> ile aynı (yabancı tenant kodu ifşa edilmez).</summary>
    private async Task<Dictionary<Guid, string>> LoadMetalCodesAsync(IEnumerable<Guid> metalIds)
    {
        var metals = await LoadVisibleMetalsAsync(metalIds);
        return metals.ToDictionary(kv => kv.Key, kv => kv.Value.Code);
    }

    /// <summary>Görünür maden kataloğu: host (TenantId=null) + kendi tenant'ı. IMultiTenant filtresi host
    /// kayıtlarını gizlediği için kapatılır ama scope AÇIKÇA daraltılır — yabancı tenant metali ne persist
    /// edilebilir ne görüntülenebilir (fail-closed).</summary>
    private async Task<Dictionary<Guid, Metal>> LoadVisibleMetalsAsync(IEnumerable<Guid> metalIds)
    {
        var ids = metalIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, Metal>();
        }

        var tenantId = CurrentTenant.Id;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var metals = await AsyncExecuter.ToListAsync(
                (await _metalRepository.GetQueryableAsync())
                    .Where(m => ids.Contains(m.Id) && (m.TenantId == null || m.TenantId == tenantId)));
            return metals.ToDictionary(m => m.Id);
        }
    }
}
