using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Attribute-driven varyant ÜRETİM/SENKRON servisi — SUNUCU tarafında (ürün kuralı 2026-07-05: üretim API'de,
/// Blazor'da değil). Ürünün attribute × değer KARTEZYENİNİ mevcut varyant setiyle mutabık kılar:
/// <list type="bullet">
/// <item>0 attribute → ürün TEK varyanttır (base; <see cref="ProductVariantManager.EnsureMainVariantAsync"/>).</item>
/// <item>Yeni kombinasyon → yeni varyant (Code/Name değer adlarından OTOMATİK türer) + bağ satırları. AKTİF doğar;
/// kullanıcı istemediğini pasife çeker (satışa sunulmaz — seçili aktivasyon).</item>
/// <item>Artık geçersiz kombinasyon (attribute/değer silindi, bağ'sız eski varyant) → varyant + bağları silinir.</item>
/// <item>Sonda tekil-main garantisi (<see cref="ProductVariantManager"/>).</item>
/// </list>
/// Company görünürlük filtresi kapatılır (OrgTreeManager deseni — sorgular ProductId ile daraltılmış, sızıntı yok).
/// </summary>
public class ProductVariantSynchronizer : DomainService
{
    private readonly IRepository<ProductAttribute, Guid> _attributeRepository;
    private readonly IRepository<ProductAttributeValue, Guid> _valueRepository;
    private readonly IRepository<ProductVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantAttributeValue, Guid> _linkRepository;
    private readonly ProductVariantManager _variantManager;
    private readonly IDataFilter _dataFilter;

    public ProductVariantSynchronizer(
        IRepository<ProductAttribute, Guid> attributeRepository,
        IRepository<ProductAttributeValue, Guid> valueRepository,
        IRepository<ProductVariant, Guid> variantRepository,
        IRepository<ProductVariantAttributeValue, Guid> linkRepository,
        ProductVariantManager variantManager,
        IDataFilter dataFilter)
    {
        _attributeRepository = attributeRepository;
        _valueRepository = valueRepository;
        _variantRepository = variantRepository;
        _linkRepository = linkRepository;
        _variantManager = variantManager;
        _dataFilter = dataFilter;
    }

    /// <summary>Ürünün varyant setini attribute/değer tanımıyla mutabık kılar (idempotent).</summary>
    public async Task SynchronizeAsync(Product product)
    {
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            var attributes = (await AsyncExecuter.ToListAsync(
                    (await _attributeRepository.GetQueryableAsync()).Where(a => a.ProductId == product.Id)))
                .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
                .ToList();

            // 0 attribute → tek base varyant; attribute'lu dönemden kalan bağlı varyantlar temizlenir.
            if (attributes.Count == 0)
            {
                await RemoveLinkedVariantsAsync(product.Id);
                await _variantManager.EnsureMainVariantAsync(product);
                return;
            }

            var attributeIds = attributes.Select(a => a.Id).ToList();
            var values = (await AsyncExecuter.ToListAsync(
                    (await _valueRepository.GetQueryableAsync()).Where(v => attributeIds.Contains(v.ProductAttributeId))))
                .OrderBy(v => v.DisplayOrder).ThenBy(v => v.Value)
                .ToList();

            var axes = attributes
                .Select(a => (Attribute: a, Values: values.Where(v => v.ProductAttributeId == a.Id).ToList()))
                .ToList();

            // Değersiz attribute varken kartezyen BOŞTUR → üretilecek kombinasyon yok; attribute'suz gibi davranma
            // (kullanıcı henüz değer giriyor olabilir) — mevcut seti koru, yalnız main'i garanti et.
            if (axes.Any(x => x.Values.Count == 0))
            {
                await _variantManager.EnsureMainVariantAsync(product);
                return;
            }

            // Hedef kombinasyonlar: her attribute'tan bir değer (kartezyen). İmza = sıralı valueId dizisi.
            var target = BuildCartesian(axes);

            var existingVariants = await AsyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync()).Where(v => v.ProductId == product.Id));
            var variantIds = existingVariants.Select(v => v.Id).ToList();
            var links = await AsyncExecuter.ToListAsync(
                (await _linkRepository.GetQueryableAsync()).Where(l => variantIds.Contains(l.ProductVariantId)));

            var existingByKey = existingVariants.ToDictionary(
                v => BuildKey(links.Where(l => l.ProductVariantId == v.Id).Select(l => l.ProductAttributeValueId)),
                v => v);

            // 1) Hedefte OLMAYAN mevcutlar (bağ'sız base'ler + kaldırılan kombinasyonlar) → sil.
            var targetKeys = target.Select(c => BuildKey(c.Select(x => x.Value.Id))).ToHashSet();
            foreach (var (key, variant) in existingByKey)
            {
                if (!targetKeys.Contains(key))
                {
                    await _linkRepository.DeleteAsync(l => l.ProductVariantId == variant.Id, autoSave: true);
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

                var variant = new ProductVariant(
                    product.CompanyId,
                    product.Id,
                    BuildVariantCode(combination.Select(x => x.Value.Value)),
                    BuildVariantName(product.Name, combination.Select(x => x.Value.Value)));
                await _variantRepository.InsertAsync(variant, autoSave: true);

                foreach (var (attribute, value) in combination)
                {
                    await _linkRepository.InsertAsync(
                        new ProductVariantAttributeValue(product.CompanyId, variant.Id, attribute.Id, value.Id),
                        autoSave: true);
                }
            }

            await _variantManager.EnsureMainVariantAsync(product);
        }
    }

    /// <summary>Attribute-bağlı TÜM varyantları (bağlarıyla) siler — attribute'lar tamamen kaldırılınca.</summary>
    private async Task RemoveLinkedVariantsAsync(Guid productId)
    {
        var variantIds = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync()).Where(v => v.ProductId == productId).Select(v => v.Id));
        if (variantIds.Count == 0)
        {
            return;
        }

        var linkedVariantIds = (await AsyncExecuter.ToListAsync(
                (await _linkRepository.GetQueryableAsync())
                    .Where(l => variantIds.Contains(l.ProductVariantId))
                    .Select(l => l.ProductVariantId)))
            .Distinct()
            .ToList();

        foreach (var id in linkedVariantIds)
        {
            await _linkRepository.DeleteAsync(l => l.ProductVariantId == id, autoSave: true);
            await _variantRepository.DeleteAsync(v => v.Id == id, autoSave: true);
        }
    }

    // Kartezyen: her attribute'tan (axis) bir değer — kombinasyon listesi ((attr,val) çiftleri, attribute sırasıyla).
    private static List<List<(ProductAttribute Attribute, ProductAttributeValue Value)>> BuildCartesian(
        List<(ProductAttribute Attribute, List<ProductAttributeValue> Values)> axes)
    {
        var result = new List<List<(ProductAttribute, ProductAttributeValue)>> { new() };
        foreach (var (attribute, axisValues) in axes)
        {
            result = result
                .SelectMany(prefix => axisValues.Select(v =>
                {
                    var next = new List<(ProductAttribute, ProductAttributeValue)>(prefix) { (attribute, v) };
                    return next;
                }))
                .ToList();
        }

        return result;
    }

    /// <summary>Kombinasyon imzası — sıralı valueId dizisi (kombinasyon eşitliği için deterministik anahtar).
    /// PUBLIC: AppService, kayıt-öncesi üretilen (Id'siz) varyantı DB kombinasyonuyla eşlerken AYNI imzayı kullanır (DRY).</summary>
    public static string BuildKey(IEnumerable<Guid> valueIds)
    {
        return string.Join("|", valueIds.OrderBy(id => id));
    }

    /// <summary>Varyant kodu değer adlarından OTOMATİK türer ("KIRMIZI-M"); üst sınıra sığdırılır (ürün-scope tekil,
    /// normalize NormalizeCode'da). Uzun değerlerde kesme çakışırsa ürün-scope unique index dostane olmayan hata
    /// verebilir — Adım 2 kapsamında kabul (değer adları kısa tutulur).
    /// PUBLIC: AppService'in persistsiz üretim önizlemesi (GenerateVariants) AYNI türetmeyi kullanır (DRY).</summary>
    public static string BuildVariantCode(IEnumerable<string> valueNames)
    {
        var joined = string.Join("-", valueNames);
        return joined.Length <= ProductConsts.CodeMaxLength ? joined : joined[..ProductConsts.CodeMaxLength];
    }

    /// <summary>PUBLIC: üretim önizlemesiyle (GenerateVariants) ad türetme paritesi — bkz. <see cref="BuildVariantCode"/>.</summary>
    public static string BuildVariantName(string productName, IEnumerable<string> valueNames)
    {
        var joined = $"{productName} {string.Join(" ", valueNames)}";
        return joined.Length <= ProductConsts.NameMaxLength ? joined : joined[..ProductConsts.NameMaxLength];
    }
}
