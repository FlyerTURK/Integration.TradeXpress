using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürün → varyant değişmezlerini tek noktada toplar (Company→HQ Branch / Branch→default Vault ile SİMETRİK):
/// her ürün en az bir varyantla + <b>tekil main</b> varyantla yaşar. UI (AppService graf-save) ve seed yolları
/// aynı invariant'ı paylaşır. Klon-ve-değiştir (varyantın zengin veriyi ana varyanttan kopyalaması) Adım 2+'de
/// (varyant reçete/fiyat/görsel taşıyınca) zenginleşir.
/// </summary>
public class ProductVariantManager : DomainService
{
    private readonly IRepository<ProductVariant, Guid> _variantRepository;
    private readonly IDataFilter _dataFilter;

    public ProductVariantManager(
        IRepository<ProductVariant, Guid> variantRepository,
        IDataFilter dataFilter)
    {
        _variantRepository = variantRepository;
        _dataFilter = dataFilter;
    }

    /// <summary>
    /// Ürünün ana (main) varyantını garanti eder (idempotent): main varsa onu döner (fazladan main'leri düşürür);
    /// varyant var ama main yoksa ilkini (en düşük Code) main'e yükseltir; hiç varyant yoksa ürün kimliğinden
    /// varsayılan main varyantı kurar. Company görünürlük filtresi kapatılır (OrgTreeManager deseni: sorgu
    /// <c>ProductId</c> ile daraltıldığı için sızıntı yok, working-context sentinel'inden etkilenmez).
    /// </summary>
    public async Task<ProductVariant> EnsureMainVariantAsync(Product product)
    {
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            var variants = (await AsyncExecuter.ToListAsync(
                    (await _variantRepository.GetQueryableAsync()).Where(v => v.ProductId == product.Id)))
                .OrderBy(v => v.Code)
                .ToList();

            var main = variants.FirstOrDefault(v => v.IsMain);
            if (main != null)
            {
                await UnsetOtherMainsAsync(product.Id, main.Id);
                return main;
            }

            if (variants.Count > 0)
            {
                var promote = variants.First();
                promote.SetAsMain(true);
                await _variantRepository.UpdateAsync(promote, autoSave: true);
                await UnsetOtherMainsAsync(product.Id, promote.Id);
                return promote;
            }

            // Base (0-attribute) ana varyant SABİT kimlikli (ürün kodundan TÜRETİLMEZ; ProductConsts SSOT).
            var variant = new ProductVariant(
                product.CompanyId,
                product.Id,
                ProductConsts.MainVariantCode,
                ProductConsts.MainVariantName,
                isMain: true);

            await _variantRepository.InsertAsync(variant, autoSave: true);
            return variant;
        }
    }

    /// <summary>Ürün başına tek main: verilen hariç diğerlerinin main bayrağını düşürür.</summary>
    private async Task UnsetOtherMainsAsync(Guid productId, Guid exceptVariantId)
    {
        var others = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.ProductId == productId && v.IsMain && v.Id != exceptVariantId));

        foreach (var o in others)
        {
            o.SetAsMain(false);
            await _variantRepository.UpdateAsync(o, autoSave: true);
        }
    }

    /// <summary>Ürünün tüm varyantlarını siler (ürün silinmeden önce çağrılır).</summary>
    public async Task DeleteVariantsOfProductAsync(Guid productId, bool autoSave = true)
    {
        await _variantRepository.DeleteAsync(v => v.ProductId == productId, autoSave: autoSave);
    }
}
