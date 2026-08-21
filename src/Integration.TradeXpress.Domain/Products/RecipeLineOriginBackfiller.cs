using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.Extensions.Logging;

namespace Integration.TradeXpress.Products;

/// <summary>
/// <see cref="RecipeLineOrigin"/> geçiş backfill'i — alan eklenmeden ÖNCE muadillikten üretilmiş reçete
/// satırlarını <see cref="RecipeLineOrigin.Substitution"/> olarak işaretler.
///
/// <para><b>Neden zorunlu:</b> yeni kolonun varsayılanı <c>Manual</c>'dır. İşaretlenmezse muadil
/// materyalizasyonu kendi eski satırlarını "kullanıcı satırı" sanar; siler-yeniden-yazar adımında eskileri
/// SİLMEZ ama yenilerini EKLER → her yeniden hesaplamada reçete KOPYALANIR. Bu backfill o kopyalanmayı
/// oluşmadan önce keser.</para>
///
/// <para><b>Neden migration DIŞINDA:</b> hangi satırın muadilden geldiğini bilmek üç tabloyu birleştirmeyi
/// gerektirir (satır → varyant → ürün.VariantMode) ve governance guard'ı migration dosyalarının elle
/// düzenlenmesini bloklar. <c>CountryReferenceBackfiller</c> deseniyle hizalı: DbMigrator'ın migrate sonrası
/// seed akışında koşar.</para>
///
/// <para><b>İdempotent ve DAR:</b> yalnız (a) <c>Origin = Manual</c>, (b) katalog-emtia + Metal ailesi,
/// (c) sahibi varyant MUADİL modundaki bir ürüne ait satırlara dokunur. Bu üç koşul muadil materyalizasyonunun
/// ürettiği satır kümesinin ta kendisidir; kullanıcının muadil ürününe ELLE eklediği metal satırı da bu kümeye
/// düşer — bilinçli taraf seçimi: kopyalanma (veri şişmesi) tek bir satırın yeniden üretilmesinden daha kötüdür.</para>
/// </summary>
public class RecipeLineOriginBackfiller : DomainService
{
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IDataFilter _dataFilter;

    public RecipeLineOriginBackfiller(
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<Product, Guid> productRepository,
        IDataFilter dataFilter)
    {
        _recipeLineRepository = recipeLineRepository;
        _variantRepository = variantRepository;
        _productRepository = productRepository;
        _dataFilter = dataFilter;
    }

    /// <summary>
    /// Tüm tenant'larda işaretlenmemiş muadil satırlarının <c>Origin</c>'ini yazar.
    ///
    /// <para><b>YALNIZ GEÇİŞ KOŞUSU</b> (<paramref name="isFirstRunAfterUpgrade"/>): seed akışı her DbMigrator
    /// koşusunda yeniden çalışır ve bu backfill'in filtresi ("Manual + katalog + Metal + muadil ürünün varyantı")
    /// geçişten SONRA kullanıcının elle eklediği metal satırlarıyla da eşleşir. Her koşuda çalışsaydı kullanıcı
    /// satırları sessizce <c>Substitution</c>'a döner ve BİR SONRAKİ muadil hesaplamasında SİLİNİRDİ — düzeltmek
    /// için eklenen mekanizmanın kendisi veri kaybına yol açardı. Bu yüzden koşul çağıranda: işaretlenmemiş
    /// muadil satırı KALMADIYSA (geçiş tamamlandıysa) tekrar çalışmaz.</para>
    /// </summary>
    public async Task BackfillAllTenantsAsync()
    {
        // Tenant + şirket filtreleri kapalı: geçiş TEK koşuda tüm veriyi kapsamalı.
        using (_dataFilter.Disable<IMultiTenant>())
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            var candidates = await _recipeLineRepository.GetListAsync(l =>
                l.Origin == RecipeLineOrigin.Manual
                && l.ComponentType == RecipeComponentType.CatalogCommodity
                && l.CommodityProcessType == ProcessType.Metal);

            if (candidates.Count == 0)
            {
                return;
            }

            var substitutionVariantIds = await LoadSubstitutionVariantIdsAsync();
            if (substitutionVariantIds.Count == 0)
            {
                return;
            }

            // GEÇİŞ TAMAMLANMIŞ VARYANTLARA DOKUNMA: bir varyantta ZATEN Substitution satırı varsa o varyant
            // geçişten geçmiştir; sonraki koşularda oradaki Manual metal satırları KULLANICININ eklediğidir ve
            // işaretlenirse bir sonraki muadil hesaplamasında silinirdi. Bu daraltma backfill'i gerçek anlamda
            // idempotent yapar (yorum değil, mekanik koruma).
            var alreadyMigratedVariantIds = (await _recipeLineRepository.GetListAsync(
                    l => l.Origin == RecipeLineOrigin.Substitution))
                .Select(l => l.ProductVariantId)
                .ToHashSet();

            var marked = 0;
            foreach (var line in candidates)
            {
                if (!substitutionVariantIds.Contains(line.ProductVariantId)
                    || alreadyMigratedVariantIds.Contains(line.ProductVariantId))
                {
                    continue;
                }

                line.SetOrigin(RecipeLineOrigin.Substitution);
                await _recipeLineRepository.UpdateAsync(line, autoSave: true);
                marked++;
            }

            if (marked > 0)
            {
                Logger.LogInformation(
                    "Reçete satırı kaynak backfill'i: {Marked} satır muadil kaynaklı olarak işaretlendi.", marked);
            }
        }
    }

    /// <summary>MUADİL modundaki ürünlerin varyant kimlikleri — satırın sahibi bu kümedeyse satır muadil
    /// materyalizasyonunun ürünüdür.</summary>
    private async Task<HashSet<Guid>> LoadSubstitutionVariantIdsAsync()
    {
        var substitutionProductIds = (await _productRepository.GetListAsync(
                p => p.VariantMode == ProductVariantMode.Substitution))
            .Select(p => p.Id)
            .ToHashSet();

        if (substitutionProductIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        // Agnostik varyant sistemi: ürünün varyantları EntityName="Product" + EntityId=ürün ile bağlıdır.
        var variants = await _variantRepository.GetListAsync(
            v => v.EntityName == ProductVariantEntityName && substitutionProductIds.Contains(v.EntityId));

        return variants.Select(v => v.Id).ToHashSet();
    }

    // Agnostik varyant sisteminde ürün bağlamının adı (ProductAppService ile aynı sabit).
    private const string ProductVariantEntityName = "Product";
}
