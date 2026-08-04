using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Çekirdek ürün kategorisi CRUD — <b>company-owned</b>. Kapsam DAİMA çalışılan şirkettir (sunucu zorlar;
/// sahiplik client'tan gelmez). Ağaç bütünlüğü <see cref="ProductCategoryTreeManager"/>'da, kalıtım da orada.
///
/// <para><b>Nitelikler MERGE edilir, replace edilmez</b> (bu servisin en kritik davranışı): gelen listedeki
/// <c>Id</c>'ler eşleşen satırları GÜNCELLER, gelmeyenler silinir, boş <c>Id</c> yeni satırdır. Sebep: nitelik
/// ve değer kimlikleri pazaryeri eşleştirmesinin hedefidir; her kaydetmede yeniden yaratmak tüm eşleştirmeleri
/// sessizce koparırdı (<c>VariantTemplate</c> JSON tutup replace edebiliyor çünkü oraya dışarıdan referans yok).</para>
/// </summary>
[Authorize(TradeXpressPermissions.ProductCategories.Default)]
public class ProductCategoryAppService : TradeXpressAppService, IProductCategoryAppService
{
    private readonly IRepository<ProductCategory, Guid> _repository;
    private readonly IRepository<Products.Product, Guid> _productRepository;   // yalnız OKUMA — silme guard'ı
    private readonly IRepository<ProductCategoryChannelMapping, Guid> _channelMappingRepository;
    private readonly IRepository<ProductCategoryChannelAttributeMapping, Guid> _channelAttributeMappingRepository;
    private readonly IRepository<ProductCategoryChannelAttributeValueMapping, Guid> _channelValueMappingRepository;
    private readonly ProductCategoryChannelResolver _channelResolver;
    private readonly ProductCategoryTreeManager _treeManager;
    private readonly ICurrentCompany _currentCompany;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Name", "IsActive", "DisplayOrder", "ParentId", "Id" };

    public ProductCategoryAppService(
        IRepository<ProductCategory, Guid> repository,
        IRepository<Products.Product, Guid> productRepository,
        IRepository<ProductCategoryChannelMapping, Guid> channelMappingRepository,
        IRepository<ProductCategoryChannelAttributeMapping, Guid> channelAttributeMappingRepository,
        IRepository<ProductCategoryChannelAttributeValueMapping, Guid> channelValueMappingRepository,
        ProductCategoryChannelResolver channelResolver,
        ProductCategoryTreeManager treeManager,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _productRepository = productRepository;
        _channelMappingRepository = channelMappingRepository;
        _channelAttributeMappingRepository = channelAttributeMappingRepository;
        _channelValueMappingRepository = channelValueMappingRepository;
        _channelResolver = channelResolver;
        _treeManager = treeManager;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<ProductCategoryListDto>> GetListAsync(ProductCategoryListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new PagedResultDto<ProductCategoryListDto>(0, new List<ProductCategoryListDto>());
        }

        var query = (await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId);

        if (input.ParentId is { } rawParentId)
        {
            // Guid.Empty "kök" demektir (entity NormalizeParent ile aynı okuma). Ham hâliyle sorgulansaydı
            // DB'de ParentId=Guid.Empty satır olmadığından kökleri istemek DAİMA boş liste dönerdi.
            var parentId = NormalizeParentId(rawParentId);
            query = query.Where(x => x.ParentId == parentId);
        }

        // YOL filtresi grid'den gelebilir ama SQL'e ÇEVRİLEMEZ: Path bir entity kolonu değil, sorgudan SONRA
        // ağaç yürünerek hesaplanıyor. Whitelist'e eklemek de mümkün değil (EF var olmayan kolonu çeviremez).
        // Bu yüzden yol filtreleri istekten AYRILIR ve hesaplamadan SONRA bellekte uygulanır.
        var pathFilters = (input.Filters ?? new List<FilterField>())
            .Where(f => string.Equals(f.Field, nameof(ProductCategoryListDto.Path), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pathFilters.Count > 0)
        {
            input.Filters = input.Filters!.Except(pathFilters).ToList();
        }

        query = query.ApplyListRequest(input, AllowedListFields);

        // Yol filtresi varsa DB'de SAYFALAMA YAPILAMAZ: filtre henüz uygulanmadığından hangi satırın elemede
        // kalacağı bilinmiyor; sayfalasaydık "1. sayfada 3 sonuç, 2. sayfada 0" gibi tutarsız bir liste çıkardı.
        // Kategori ağacı küçük bir taksonomidir (onlarca–yüzlerce satır) → tamamını çekip bellekte elemek makul;
        // ürün/sipariş gibi büyük tablolarda bu YAPILMAZ.
        if (pathFilters.Count == 0)
        {
            var totalCount = await AsyncExecuter.CountAsync(query);
            var items = await AsyncExecuter.ToListAsync(query.ApplyPaging(input));

            var rows = items.Select(e => ObjectMapper.Map<ProductCategory, ProductCategoryListDto>(e)).ToList();
            await FillPathsAsync(companyId, rows);

            return new PagedResultDto<ProductCategoryListDto>(totalCount, rows);
        }

        var allItems = await AsyncExecuter.ToListAsync(query);
        var allRows = allItems.Select(e => ObjectMapper.Map<ProductCategory, ProductCategoryListDto>(e)).ToList();
        await FillPathsAsync(companyId, allRows);

        var filtered = allRows.Where(r => pathFilters.All(f => MatchesPath(r.Path, f))).ToList();

        // Sayfalama filtreden SONRA — toplam sayı da elenmiş küme üzerinden verilir ki pager doğru olsun.
        // Elle Skip/Take YAZILMAZ: ApplyPaging'in IEnumerable aşırı yüklemesi "Tümü" (AllPages) semantiğini
        // doğru ele alıyor; elle yazılan Take(-1) LINQ'te sessizce BOŞ liste döndürürdü (PagingConventionTests).
        var page = filtered.ApplyPaging(input).ToList();

        return new PagedResultDto<ProductCategoryListDto>(filtered.Count, page);
    }

    /// <summary>
    /// Hesaplanmış YOL üzerinde tek bir kolon filtresini uygular.
    ///
    /// <para>Karşılaştırma <see cref="SearchNormalizer"/> ile katlanır — kullanıcı "taki" yazınca "Takı" da
    /// bulunur (Türkçe I/ı ve aksan sorunları grid aramasının en sık şikâyeti). Sunucudaki metin aramasıyla
    /// AYNI kural; ikisi ayrışsa aynı terim bir kolonda bulur diğerinde bulmazdı.</para></summary>
    private static bool MatchesPath(string? path, FilterField filter)
    {
        var haystack = SearchNormalizer.Fold(path ?? string.Empty);
        var needle = SearchNormalizer.Fold(filter.Value ?? string.Empty);

        if (needle.Length == 0)
        {
            return true;   // boş filtre eleme yapmaz
        }

        return filter.Operator switch
        {
            ListFilterOperator.Equals => string.Equals(haystack, needle, StringComparison.Ordinal),
            ListFilterOperator.NotEquals => !string.Equals(haystack, needle, StringComparison.Ordinal),
            ListFilterOperator.StartsWith => haystack.StartsWith(needle, StringComparison.Ordinal),
            ListFilterOperator.EndsWith => haystack.EndsWith(needle, StringComparison.Ordinal),
            // Contains + sayısal operatörler: yol METİNDİR, büyük/küçük karşılaştırması anlamsız → Contains'e düşer.
            _ => haystack.Contains(needle, StringComparison.Ordinal),
        };
    }

    /// <summary>
    /// Düz listedeki satırlara ağaç yolunu ve seviyesini yazar. Şirketin kategori ağacı TEK hafif sorguyla
    /// (id/parent/ad) belleğe alınır; satır başına zincir yürümek N+1 üretirdi. Kategori sayısı şirket başına
    /// yüzler mertebesindedir — sayfalanmış satırların yolunu kurmak için ağacın tamamı zaten gereklidir.
    /// </summary>
    private async Task FillPathsAsync(Guid companyId, List<ProductCategoryListDto> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var tree = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId)
                .Select(x => new { x.Id, x.ParentId, x.Name }));

        var byId = tree.ToDictionary(x => x.Id);

        // Eşleştirmesi OLAN kategorilerin kimlikleri — tek hafif sorgu. Ata zinciri zaten aşağıda yürünüyor,
        // bayrak orada bedavaya çözülür (satır başına ayrı sorgu N+1 olurdu).
        var mappedCategoryIds = (await AsyncExecuter.ToListAsync(
            (await _channelMappingRepository.GetQueryableAsync())
                .Select(m => m.ProductCategoryId))).ToHashSet();

        foreach (var row in rows)
        {
            var segments = new List<string>();
            var visited = new HashSet<Guid>();
            var current = row.Id;
            var hasMapping = false;

            // Bozuk veride (elle düzenlenmiş DB) döngü olsa bile ziyaret işareti yürüyüşü keser.
            while (byId.TryGetValue(current, out var node) && visited.Add(current))
            {
                segments.Add(node.Name);
                hasMapping = hasMapping || mappedCategoryIds.Contains(current);
                current = node.ParentId ?? Guid.Empty;
            }

            segments.Reverse();
            row.Path = string.Join(" › ", segments);
            row.Level = Math.Max(0, segments.Count - 1);
            row.HasChannelMapping = hasMapping;
        }
    }

    public virtual async Task<ProductCategoryGetDto> GetAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        return await MapToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.ProductCategories.Create)]
    public virtual async Task<ProductCategoryGetDto> CreateAsync(ProductCategoryCreateDto input)
    {
        var companyId = ResolveCompanyId();

        await _treeManager.EnsureParentIsValidAsync(companyId, Guid.Empty, input.ParentId);
        await EnsureNameUniqueAmongSiblingsAsync(companyId, input.ParentId, NormalizeName(input.Name), Guid.Empty);

        var entity = new ProductCategory(companyId, input.Name, input.ParentId, input.DisplayOrder);
        entity.SetDescription(input.Description);
        ProductCategoryAttributeMerger.Apply(entity, input.Attributes);

        await _repository.InsertAsync(entity, autoSave: true);
        return await MapToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.ProductCategories.Update)]
    public virtual async Task<ProductCategoryGetDto> UpdateAsync(Guid id, ProductCategoryUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);

        var normalizedName = NormalizeName(input.Name);
        var parentChanged = entity.ParentId != input.ParentId;

        if (parentChanged)
        {
            await _treeManager.EnsureParentIsValidAsync(entity.CompanyId, entity.Id, input.ParentId);
        }

        // Ad VEYA üst değişmişse kardeş benzersizliği yeniden sınanır: ikisi de aynı kovayı değiştirir
        // (taşınan kategori hedefte var olan bir adla çakışabilir).
        if (parentChanged || !string.Equals(normalizedName, entity.Name, StringComparison.Ordinal))
        {
            await EnsureNameUniqueAmongSiblingsAsync(entity.CompanyId, input.ParentId, normalizedName, entity.Id);
        }

        if (parentChanged)
        {
            entity.SetParent(input.ParentId);
        }

        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        entity.SetDisplayOrder(input.DisplayOrder);
        entity.SetActive(input.IsActive);
        ProductCategoryAttributeMerger.Apply(entity, input.Attributes);

        await _repository.UpdateAsync(entity, autoSave: true);
        return await MapToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.ProductCategories.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);

        // Çocuğu olan kategori silinemez: silinseydi alt dal öksüz kalır ve hiçbir ekranda görünmezdi
        // (kaskad silmek ise kullanıcının görmediği kayıtları yok etmek olurdu — önce çocuklar taşınsın).
        var hasChildren = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.ParentId == entity.Id));
        if (hasChildren)
        {
            throw new BusinessException("TradeXpress:ProductCategory:HasChildren");
        }

        // Ürün bağlıysa da silinemez: bağ id-only olduğundan (sert FK yok) DB engellemezdi ve ürünler var
        // olmayan bir kategoriyi işaret ederdi — kanal kategorisi ve komisyon çözümü o üründe sessizce boşa düşerdi.
        var inUse = await AsyncExecuter.AnyAsync(
            (await _productRepository.GetQueryableAsync()).Where(p => p.ProductCategoryId == entity.Id));
        if (inUse)
        {
            throw new BusinessException("TradeXpress:ProductCategory:InUseByProducts");
        }

        // Kanal eşleştirmeleri kategoriyle birlikte gider: sert FK olmadığından DB temizlemez ve öksüz satır
        // kalırdı — kategori id'si yeniden kullanılmasa da bu satırlar hiçbir ekranda görünmeden birikirdi.
        await _channelMappingRepository.DeleteAsync(m => m.ProductCategoryId == entity.Id, autoSave: true);
        await _channelAttributeMappingRepository.DeleteAsync(m => m.ProductCategoryId == entity.Id, autoSave: true);
        await _channelValueMappingRepository.DeleteAsync(m => m.ProductCategoryId == entity.Id, autoSave: true);

        await _repository.DeleteAsync(entity);
    }

    public virtual async Task<List<ProductCategoryListDto>> GetPickerListAsync()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new List<ProductCategoryListDto>();
        }

        var entities = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name));

        var rows = entities.Select(e => ObjectMapper.Map<ProductCategory, ProductCategoryListDto>(e)).ToList();

        // Üst kategori combo'su bunu kullanır: yol olmadan aynı adlı iki alt kategori ayırt edilemez.
        await FillPathsAsync(companyId, rows);

        return rows.OrderBy(r => r.Path, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// "Üst kategori" combo'sunun seçenekleri. <paramref name="excludeId"/> verilen kategorinin KENDİSİ ve
    /// TÜM ALT AĞACI listeden düşer — bir kategori kendi torununu üst seçemez (döngü).
    ///
    /// <para><b>Neden istemcide değil burada:</b> alt ağaç hesabı TAM ağacı görmeyi gerektirir. Picker yalnız
    /// aktif kategorileri döndürdüğünden, <c>A(aktif) → B(pasif) → C(aktif)</c> zincirinde istemci C'nin A'nın
    /// torunu olduğunu göremez ve C'yi seçilebilir sanardı. Sunucu ağacı pasifler dahil okuyup dışlamayı
    /// ona göre yapar; dönen liste yine yalnız aktifleri içerir.</para>
    /// </summary>
    public virtual async Task<List<ProductCategoryListDto>> GetParentOptionsAsync(Guid? excludeId)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new List<ProductCategoryListDto>();
        }

        // Ağacın TAMAMI (pasifler dahil) TEK sorguda: hem dışlama kümesi hem de yol hesabı bunu gerektirir.
        var entities = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.CompanyId == companyId));

        var rows = entities.Select(e => ObjectMapper.Map<ProductCategory, ProductCategoryListDto>(e)).ToList();
        await FillPathsAsync(companyId, rows);

        var self = excludeId is { } id && id != Guid.Empty ? id : Guid.Empty;
        var excluded = self != Guid.Empty
            ? ProductCategoryTreeManager.CollectSubtreeIds(rows.Select(r => (r.Id, r.ParentId)), self)
            : new HashSet<Guid>();

        // Kategorinin ŞU ANKİ üstü — pasifleşmiş olsa bile listede kalmalı. Aksi hâlde combo boş görünür,
        // kullanıcı üstünü kaybetmiş sanıp yeniden seçmeye kalkar (ya da farkında olmadan kökte bırakır).
        var currentParentId = rows.FirstOrDefault(r => r.Id == self)?.ParentId;

        return rows
            .Where(r => !excluded.Contains(r.Id))
            .Where(r => r.IsActive || r.Id == currentParentId)
            .OrderBy(r => r.Path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public virtual async Task<List<ProductCategoryEffectiveAttributeDto>> GetEffectiveAttributesAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        var effective = await _treeManager.GetEffectiveAttributesAsync(entity.CompanyId, entity.Id);
        return effective.Select(MapEffective).ToList();
    }

    public virtual async Task<List<ProductCategoryAttributeDto>> PreviewInheritanceAsync(
        ProductCategoryInheritancePreviewDto input)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new List<ProductCategoryAttributeDto>();
        }

        var parentId = NormalizeParentId(input.ParentId);
        var path = parentId is { } id
            ? await _treeManager.GetPathAsync(companyId, id)
            : new List<ProductCategory>();

        // Formdaki KENDİ nitelikleri geçici (kaydedilmeyen) bir kategoriye kurulur ve zincirin sonuna eklenir →
        // birleştirme, kayıt sonrası Get ile BİREBİR aynı kuralı kullanır. Bu entity repository'ye HİÇ verilmez.
        var draft = new ProductCategory(companyId, "PREVIEW", parentId);
        ProductCategoryAttributeMerger.Apply(draft, input.OwnAttributes);
        path.Add(draft);

        var preview = ProductCategoryTreeManager.MergeAttributes(path, draft.Id)
            .Select(MapEffectiveToEditable)
            .ToList();

        RestoreOwnIdentities(preview, input.OwnAttributes);
        return preview;
    }

    /// <summary>
    /// Önizleme sonucundaki KENDİ satırlarına formdan gelen kalıcı kimlikleri geri yazar.
    ///
    /// <para><b>Neden zorunlu:</b> önizleme kaydedilmeyen bir taslak üzerinden hesaplanır, dolayısıyla oradaki
    /// satırların <c>Id</c>'si boştur. Bu hâliyle forma dönseydi kullanıcının MEVCUT nitelikleri kimliksiz
    /// kalır ve bir sonraki kaydetmede yeni satır olarak yazılırdı — kimlikler kopar, pazaryeri eşleştirmeleri
    /// sessizce boşa düşerdi. Eşleme AD üzerinden yapılır çünkü birleştirmenin kendisi de ad tabanlıdır
    /// (<c>OrdinalIgnoreCase</c>); devralınan satır/değerlere DOKUNULMAZ, onların kimliği sahibine aittir.</para>
    /// </summary>
    private static void RestoreOwnIdentities(
        List<ProductCategoryAttributeDto> preview,
        List<ProductCategoryAttributeDto> ownAttributes)
    {
        var ownByName = ownAttributes
            .Where(a => !a.IsInherited && !string.IsNullOrWhiteSpace(a.Name))
            .GroupBy(a => a.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in preview.Where(a => !a.IsInherited))
        {
            if (!ownByName.TryGetValue(attribute.Name.Trim(), out var source))
            {
                continue;
            }

            attribute.Id = source.Id;

            var ownValuesByText = source.Values
                .Where(v => !v.IsInherited && !string.IsNullOrWhiteSpace(v.Value))
                .GroupBy(v => v.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var value in attribute.Values.Where(v => !v.IsInherited))
            {
                if (ownValuesByText.TryGetValue(value.Value.Trim(), out var sourceValue))
                {
                    value.Id = sourceValue.Id;
                }
            }
        }
    }

    public virtual async Task<List<ProductCategoryChannelMappingDto>> GetChannelMappingsAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);

        var mappings = await AsyncExecuter.ToListAsync(
            (await _channelMappingRepository.GetQueryableAsync())
                .Where(m => m.CompanyId == entity.CompanyId && m.ProductCategoryId == entity.Id));

        var result = new List<ProductCategoryChannelMappingDto>();
        foreach (var mapping in mappings.OrderBy(m => m.Channel))
        {
            result.Add(new ProductCategoryChannelMappingDto
            {
                Id = mapping.Id,
                ProductCategoryId = mapping.ProductCategoryId,
                Channel = mapping.Channel,
                ChannelCategoryExternalId = mapping.ChannelCategoryExternalId,
                ChannelCategoryName = mapping.ChannelCategoryName,
                // Oranı burada da çözeriz: kullanıcı eşleştirmenin fiyata etkisini listede görsün
                // (kanal ürünü oluşturmayı beklemeden).
                EffectiveCommissionRate = await _channelResolver.ResolveCommissionRateAsync(
                    entity.CompanyId, entity.Id, mapping.Channel, channelDefaultRate: null),
                AttributeMappings = await LoadAttributeMappingsAsync(entity, mapping.Channel),
            });
        }

        return result;
    }

    /// <summary>
    /// Bir kanalin nitelik eslestirmelerini FORM icin okur - satirlar kategorinin ETKIN niteliklerinden
    /// (kalitim cozulmus) turetilir, kayitli eslestirmelerden degil.
    ///
    /// <para><b>Neden:</b> henuz hic eslestirilmemis nitelik de listede gorunmeli ki kullanici eslestirebilsin;
    /// kayitlardan turetilseydi ilk acilista bos bir tablo cikar ve eslestirme hic baslatilamazdi.</para>
    ///
    /// <para>VARYANT ekseni nitelikleri DISLANIR: onlarin degerleri varyantin kendisinde yasar ve kanala
    /// varyant (stok kalemi) olarak gider - urun-seviyesi nitelik degildir.</para>
    /// </summary>
    private async Task<List<ProductCategoryChannelAttributeMappingDto>> LoadAttributeMappingsAsync(
        ProductCategory entity, SalesChannelType channel)
    {
        var effective = await _treeManager.GetEffectiveAttributesAsync(entity.CompanyId, entity.Id);
        var specifications = effective
            .Where(a => a.Kind == ProductCategoryAttributeKind.Specification)
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
            .ToList();
        if (specifications.Count == 0)
        {
            return new List<ProductCategoryChannelAttributeMappingDto>();
        }

        var saved = await AsyncExecuter.ToListAsync(
            (await _channelAttributeMappingRepository.GetQueryableAsync())
                .Where(m => m.CompanyId == entity.CompanyId
                    && m.ProductCategoryId == entity.Id
                    && m.Channel == channel));
        var savedById = saved.ToDictionary(m => m.ProductCategoryAttributeId);

        // Değer eşleştirmeleri TEK sorguda (nitelik başına ayrı sorgu N+1 olurdu).
        var savedValues = await AsyncExecuter.ToListAsync(
            (await _channelValueMappingRepository.GetQueryableAsync())
                .Where(m => m.CompanyId == entity.CompanyId
                    && m.ProductCategoryId == entity.Id
                    && m.Channel == channel));
        var savedValuesById = savedValues.ToDictionary(m => m.ProductCategoryAttributeValueId);

        return specifications
            .Select(a => new ProductCategoryChannelAttributeMappingDto
            {
                ProductCategoryAttributeId = a.AttributeId,
                AttributeName = a.Name,
                ChannelAttributeExternalId = savedById.GetValueOrDefault(a.AttributeId)?.ChannelAttributeExternalId,
                ChannelAttributeName = savedById.GetValueOrDefault(a.AttributeId)?.ChannelAttributeName,
                // Satırlar kategorinin DEĞER tanımından türetilir (kayıtlardan değil): henüz eşleştirilmemiş
                // değer de görünmeli ki kullanıcı eşleştirebilsin.
                ValueMappings = a.Values
                    .OrderBy(v => v.DisplayOrder)
                    .Select(v => new ProductCategoryChannelAttributeValueMappingDto
                    {
                        ProductCategoryAttributeValueId = v.ValueId,
                        ValueText = v.Value,
                        ChannelValueExternalId = savedValuesById.GetValueOrDefault(v.ValueId)?.ChannelAttributeValueExternalId,
                        ChannelValueName = savedValuesById.GetValueOrDefault(v.ValueId)?.ChannelAttributeValueName,
                    })
                    .ToList(),
            })
            .ToList();
    }

    /// <summary>
    /// Değer eşleştirmelerini saklar — gelen liste o kanal için TAM kümedir (diff: güncelle / ekle / sil).
    ///
    /// <para>Kategoride ARTIK BULUNMAYAN değer kimlikleri REDDEDİLİR; kanal değeri boş bırakılan satır da
    /// eşleştirme değildir ve silinir (boş satır push'ta sessizce atlanan kayıt üretirdi).</para>
    ///
    /// <para>NİTELİĞİ eşleştirilmemiş bir değerin eşleştirmesi de KABUL EDİLMEZ: hedef nitelik bilinmeden
    /// değer kimliğinin hangi alana yazılacağı belirsizdir.</para>
    /// </summary>
    private async Task SaveAttributeValueMappingsAsync(
        ProductCategory entity,
        SalesChannelType channel,
        List<ProductCategoryChannelAttributeMappingDto> input)
    {
        var effective = await _treeManager.GetEffectiveAttributesAsync(entity.CompanyId, entity.Id);
        var allowedValueIds = effective
            .Where(a => a.Kind == ProductCategoryAttributeKind.Specification)
            .SelectMany(a => a.Values.Select(v => v.ValueId))
            .ToHashSet();

        var incoming = (input ?? new List<ProductCategoryChannelAttributeMappingDto>())
            .Where(a => !string.IsNullOrWhiteSpace(a.ChannelAttributeExternalId))
            .SelectMany(a => a.ValueMappings ?? new List<ProductCategoryChannelAttributeValueMappingDto>())
            .Where(v => allowedValueIds.Contains(v.ProductCategoryAttributeValueId))
            .Where(v => !string.IsNullOrWhiteSpace(v.ChannelValueExternalId))
            .GroupBy(v => v.ProductCategoryAttributeValueId)
            .ToDictionary(g => g.Key, g => g.First());

        var existing = await AsyncExecuter.ToListAsync(
            (await _channelValueMappingRepository.GetQueryableAsync())
                .Where(m => m.CompanyId == entity.CompanyId
                    && m.ProductCategoryId == entity.Id
                    && m.Channel == channel));

        foreach (var row in existing)
        {
            if (incoming.TryGetValue(row.ProductCategoryAttributeValueId, out var dto))
            {
                row.SetChannelValue(dto.ChannelValueExternalId!, dto.ChannelValueName);
                await _channelValueMappingRepository.UpdateAsync(row, autoSave: true);
                incoming.Remove(row.ProductCategoryAttributeValueId);
            }
            else
            {
                await _channelValueMappingRepository.DeleteAsync(row, autoSave: true);
            }
        }

        foreach (var dto in incoming.Values)
        {
            await _channelValueMappingRepository.InsertAsync(
                new ProductCategoryChannelAttributeValueMapping(
                    entity.CompanyId,
                    entity.Id,
                    channel,
                    dto.ProductCategoryAttributeValueId,
                    dto.ChannelValueExternalId!,
                    dto.ChannelValueName),
                autoSave: true);
        }
    }

    /// <summary>
    /// Nitelik eslestirmelerini saklar - gelen liste o kanal icin TAM kumedir (diff: guncelle / ekle / sil).
    ///
    /// <para>Kanal niteligi BOS birakilan satir eslestirme DEGILDIR -> kaydi silinir. Bos satir saklamak,
    /// push tarafinda "eslestirilmis ama hedefi yok" gibi gorunen ve sessizce atlanan kayitlar uretirdi.</para>
    ///
    /// <para>Kategoride ARTIK BULUNMAYAN nitelik kimlikleri REDDEDILIR: dogrulanmasaydi istemci keyfi bir
    /// kimlikle satir yazabilir, o satir hicbir formda gorunmeden birikirdi.</para>
    /// </summary>
    private async Task SaveAttributeMappingsAsync(
        ProductCategory entity,
        SalesChannelType channel,
        List<ProductCategoryChannelAttributeMappingDto> input)
    {
        var effective = await _treeManager.GetEffectiveAttributesAsync(entity.CompanyId, entity.Id);
        var allowedIds = effective
            .Where(a => a.Kind == ProductCategoryAttributeKind.Specification)
            .Select(a => a.AttributeId)
            .ToHashSet();

        var incoming = (input ?? new List<ProductCategoryChannelAttributeMappingDto>())
            .Where(x => allowedIds.Contains(x.ProductCategoryAttributeId))
            .Where(x => !string.IsNullOrWhiteSpace(x.ChannelAttributeExternalId))
            .GroupBy(x => x.ProductCategoryAttributeId)
            .ToDictionary(g => g.Key, g => g.First());

        var existing = await AsyncExecuter.ToListAsync(
            (await _channelAttributeMappingRepository.GetQueryableAsync())
                .Where(m => m.CompanyId == entity.CompanyId
                    && m.ProductCategoryId == entity.Id
                    && m.Channel == channel));

        foreach (var row in existing)
        {
            if (incoming.TryGetValue(row.ProductCategoryAttributeId, out var dto))
            {
                row.SetChannelAttribute(dto.ChannelAttributeExternalId!, dto.ChannelAttributeName);
                await _channelAttributeMappingRepository.UpdateAsync(row, autoSave: true);
                incoming.Remove(row.ProductCategoryAttributeId);
            }
            else
            {
                await _channelAttributeMappingRepository.DeleteAsync(row, autoSave: true);
            }
        }

        foreach (var dto in incoming.Values)
        {
            await _channelAttributeMappingRepository.InsertAsync(
                new ProductCategoryChannelAttributeMapping(
                    entity.CompanyId,
                    entity.Id,
                    channel,
                    dto.ProductCategoryAttributeId,
                    dto.ChannelAttributeExternalId!,
                    dto.ChannelAttributeName),
                autoSave: true);
        }
    }


    [Authorize(TradeXpressPermissions.ProductCategories.Update)]
    public virtual async Task<ProductCategoryChannelMappingDto> SaveChannelMappingAsync(
        Guid id,
        ProductCategoryChannelMappingSaveDto input)
    {
        var entity = await _repository.GetAsync(id);

        // Kanal başına TEK satır (DB'de de unique): var olan güncellenir, yoksa kurulur. Yeni satır açmak
        // "hangisi geçerli" belirsizliği yaratır ve komisyon çözümünü rastgele kılardı.
        var existing = await _channelMappingRepository.FindAsync(
            m => m.CompanyId == entity.CompanyId
                && m.ProductCategoryId == entity.Id
                && m.Channel == input.Channel);

        if (existing is null)
        {
            existing = new ProductCategoryChannelMapping(
                entity.CompanyId, entity.Id, input.Channel, input.ChannelCategoryExternalId);
            existing.SetChannelCategory(input.ChannelCategoryExternalId, input.ChannelCategoryName);
            await _channelMappingRepository.InsertAsync(existing, autoSave: true);
        }
        else
        {
            existing.SetChannelCategory(input.ChannelCategoryExternalId, input.ChannelCategoryName);
            await _channelMappingRepository.UpdateAsync(existing, autoSave: true);
        }

        await SaveAttributeMappingsAsync(entity, input.Channel, input.AttributeMappings);
        await SaveAttributeValueMappingsAsync(entity, input.Channel, input.AttributeMappings);

        return new ProductCategoryChannelMappingDto
        {
            Id = existing.Id,
            ProductCategoryId = existing.ProductCategoryId,
            Channel = existing.Channel,
            ChannelCategoryExternalId = existing.ChannelCategoryExternalId,
            ChannelCategoryName = existing.ChannelCategoryName,
            EffectiveCommissionRate = await _channelResolver.ResolveCommissionRateAsync(
                entity.CompanyId, entity.Id, existing.Channel, channelDefaultRate: null),
            AttributeMappings = await LoadAttributeMappingsAsync(entity, existing.Channel),
        };
    }

    [Authorize(TradeXpressPermissions.ProductCategories.Update)]
    public virtual async Task DeleteChannelMappingAsync(Guid id, SalesChannelType channel)
    {
        var entity = await _repository.GetAsync(id);

        var existing = await _channelMappingRepository.FindAsync(
            m => m.CompanyId == entity.CompanyId && m.ProductCategoryId == entity.Id && m.Channel == channel);
        if (existing is not null)
        {
            // Kaldırınca kategori ATASININ eşleştirmesini devralmaya döner (kalıtım) — bu yüzden silme
            // "eşleştirme yok" değil "kendi tanımını kaldır" demektir.
            await _channelMappingRepository.DeleteAsync(existing, autoSave: true);

            // Nitelik eslestirmeleri kategori eslestirmesine BAGLIDIR: kanal kategorisi kalkinca hedef
            // taksonomi de kalkar, satirlar hicbir ekranda gorunmeden birikirdi.
            await _channelAttributeMappingRepository.DeleteAsync(
                m => m.CompanyId == entity.CompanyId && m.ProductCategoryId == entity.Id && m.Channel == channel,
                autoSave: true);
            await _channelValueMappingRepository.DeleteAsync(
                m => m.CompanyId == entity.CompanyId && m.ProductCategoryId == entity.Id && m.Channel == channel,
                autoSave: true);
        }
    }

    public virtual async Task<ProductChannelResolutionDto> ResolveChannelAsync(Guid id, SalesChannelType channel)
    {
        var entity = await _repository.GetAsync(id);

        var mapping = await _channelResolver.ResolveMappingAsync(entity.CompanyId, entity.Id, channel);
        if (mapping is null)
        {
            return new ProductChannelResolutionDto { Channel = channel };
        }

        var source = await _repository.FindAsync(mapping.ProductCategoryId);

        return new ProductChannelResolutionDto
        {
            Channel = channel,
            SourceCategoryId = mapping.ProductCategoryId,
            SourceCategoryName = source?.Name,
            IsInherited = mapping.ProductCategoryId != entity.Id,
            ChannelCategoryExternalId = mapping.ChannelCategoryExternalId,
            ChannelCategoryName = mapping.ChannelCategoryName,
            EffectiveCommissionRate = await _channelResolver.ResolveCommissionRateAsync(
                entity.CompanyId, entity.Id, channel, channelDefaultRate: null),
        };
    }

    private Guid ResolveCompanyId()
    {
        // Sahiplik client'tan DEĞİL aktif working company'den damgalanır (şirket yoksa fail-closed).
        return CompanyOwnershipGuard.ResolveOwnerCompanyId(_currentCompany);
    }

    private async Task<ProductCategoryGetDto> MapToGetDtoAsync(ProductCategory entity)
    {
        var dto = ObjectMapper.Map<ProductCategory, ProductCategoryGetDto>(entity);

        var path = await _treeManager.GetPathAsync(entity.CompanyId, entity.Id);
        dto.Path = string.Join(" › ", path.Select(c => c.Name));

        // TEK LİSTE (2026-07-28 Hakan): grid devralınanları da gösterir. Kalıtım birleştirmesi aynı adlı
        // nitelikleri zaten tek satırda toplar ve her değerin kaynağını taşır; burada yaptığımız o sonucu
        // düzenlenebilir DTO'ya çevirmek. Devralınan satır/değerler işaretlenir — UI onları kilitler, kaydetme
        // yolu da yok sayar (ProductCategoryAttributeMerger).
        dto.Attributes = ProductCategoryTreeManager.MergeAttributes(path, entity.Id)
            .Select(MapEffectiveToEditable)
            .ToList();

        return dto;
    }

    /// <summary>
    /// Kalıtım çözülmüş niteliği DÜZENLENEBİLİR DTO'ya çevirir — grid tek liste gösterdiğinden devralınan ve
    /// kendi satırlar aynı tipte taşınır, ayrım <c>IsInherited</c> ile yapılır.
    ///
    /// <para><b>Kimlik inceliği:</b> devralınan satırın <c>Id</c>'si ÜST kategorinin nitelik satırına aittir.
    /// Kaydetmede bu Id'ye asla dokunulmaz (merger devralınanı yok sayar) — aksi hâlde alt kategoride yapılan
    /// bir düzenleme üstteki niteliği değiştirirdi.</para>
    /// </summary>
    private static ProductCategoryAttributeDto MapEffectiveToEditable(ProductCategoryEffectiveAttribute source)
    {
        return new ProductCategoryAttributeDto
        {
            Id = source.IsInherited ? Guid.Empty : source.AttributeId,
            Name = source.Name,
            Kind = source.Kind,
            DisplayOrder = source.DisplayOrder,
            IsInherited = source.IsInherited,
            SourceCategoryName = source.SourceCategoryName,
            Values = source.Values
                .Select(v => new ProductCategoryAttributeValueDto
                {
                    Id = v.IsInherited ? Guid.Empty : v.ValueId,
                    Value = v.Value,
                    DisplayOrder = v.DisplayOrder,
                    IsInherited = v.IsInherited,
                    SourceCategoryName = v.SourceCategoryName,
                })
                .ToList(),
        };
    }

    private static ProductCategoryEffectiveAttributeDto MapEffective(ProductCategoryEffectiveAttribute source)
    {
        return new ProductCategoryEffectiveAttributeDto
        {
            AttributeId = source.AttributeId,
            Name = source.Name,
            Kind = source.Kind,
            DisplayOrder = source.DisplayOrder,
            SourceCategoryId = source.SourceCategoryId,
            SourceCategoryName = source.SourceCategoryName,
            IsInherited = source.IsInherited,
            Values = source.Values
                .Select(v => new ProductCategoryEffectiveAttributeValueDto
                {
                    ValueId = v.ValueId,
                    Value = v.Value,
                    DisplayOrder = v.DisplayOrder,
                    SourceCategoryId = v.SourceCategoryId,
                    SourceCategoryName = v.SourceCategoryName,
                    IsInherited = v.IsInherited,
                })
                .ToList(),
        };
    }

    // Ad, entity'nin uyguladığı normalizasyonun AYNISINDAN geçirilir (TitleCase + boşluk sadeleştirme):
    // aksi hâlde "yüzük" ile "Yüzük" ön-kontrolde farklı görünüp DB unique index'ine ham hata olarak düşerdi.
    private static string NormalizeName(string rawName)
    {
        return StringFieldGuard.NormalizeName(
            rawName, nameof(ProductCategory.Name), EntityFieldConsts.NameMinLength, ProductCategoryConsts.NameMaxLength);
    }

    /// <summary>
    /// Kardeş benzersizliği: aynı üst altında aynı ad iki kez olamaz (kod alanı olmadığından kimlik budur).
    /// Kapsam KASITLA şirket-geneli değil kardeş düzeyidir — "Takı › Yüzük" ile "Saat › Yüzük" ikisi de meşru.
    /// </summary>
    // Combo "seçilmedi" için Guid.Empty gönderebilir; entity'deki NormalizeParent ile AYNI okuma: kök.
    // Tek yerde durur ki ebeveyn okuyan her yol (liste filtresi, benzersizlik, seçenekler) aynı davransın.
    private static Guid? NormalizeParentId(Guid? parentId)
    {
        return parentId is { } value && value != Guid.Empty ? value : null;
    }

    private async Task EnsureNameUniqueAmongSiblingsAsync(
        Guid companyId,
        Guid? parentId,
        string normalizedName,
        Guid excludeId)
    {
        var normalizedParent = NormalizeParentId(parentId);

        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId
                    && x.Id != excludeId
                    && x.ParentId == normalizedParent
                    && x.Name == normalizedName));

        if (duplicate)
        {
            throw new BusinessException("TradeXpress:ProductCategory:NameAlreadyExists")
                .WithData("name", normalizedName);
        }
    }
}
