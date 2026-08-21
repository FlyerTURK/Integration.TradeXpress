using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// TRENDYOL KANAL-ÜRÜN TEST FİKSTÜRÜ — "kanal + ürün + iki doğrulanmış ERP varyantı (+ isteğe bağlı dondurulmuş
/// SKU'lar)" kurulumu TEK yerde. Eskiden <c>SalesChannelTrTrendyolProductStockSyncTests</c>'in özel yardımcısıydı;
/// batch-durum çözücüsü testleri (2026-08-19) aynı fikstürü isteyince çıkarıldı. Test sınıfları miras yerine
/// DI ile alır (miras, taban sınıfın tüm [Fact]'lerini ikinci kez koşturur).
/// </summary>
public class TrendyolChannelProductTestSeeder : ITransientDependency
{
    private const string ProductEntityName = "Product";

    private readonly ISalesChannelTrTrendyolProductAppService _appService;
    private readonly EntityVariantSynchronizer _erpSynchronizer;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _channelProductRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityAttribute, Guid> _erpAttributeRepository;
    private readonly IRepository<EntityAttributeValue, Guid> _erpValueRepository;
    private readonly IRepository<EntityVariant, Guid> _erpVariantRepository;
    private readonly IRepository<ProductVariantDetail, Guid> _variantDetailRepository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public TrendyolChannelProductTestSeeder(
        ISalesChannelTrTrendyolProductAppService appService,
        EntityVariantSynchronizer erpSynchronizer,
        IRepository<SalesChannelTrTrendyol, Guid> channelRepository,
        IRepository<SalesChannelTrTrendyolProduct, Guid> channelProductRepository,
        IRepository<Product, Guid> productRepository,
        IRepository<EntityAttribute, Guid> erpAttributeRepository,
        IRepository<EntityAttributeValue, Guid> erpValueRepository,
        IRepository<EntityVariant, Guid> erpVariantRepository,
        IRepository<ProductVariantDetail, Guid> variantDetailRepository,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _appService = appService;
        _erpSynchronizer = erpSynchronizer;
        _channelRepository = channelRepository;
        _channelProductRepository = channelProductRepository;
        _productRepository = productRepository;
        _erpAttributeRepository = erpAttributeRepository;
        _erpValueRepository = erpValueRepository;
        _erpVariantRepository = erpVariantRepository;
        _variantDetailRepository = variantDetailRepository;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary>Kanal + ürün + iki ERP varyantı (RED 10 adet, BLUE 20 adet, ikisi de 100 TL) kurar.
    /// <paramref name="verify"/> false ise varyantlar İNSAN onayından geçmemiş sayılır (guard testleri).
    /// <paramref name="seedSkus"/> true ise kayıt "daha önce push edilmiş" gibi barkodlu SKU satırları alır.
    /// Çağıran <c>ICurrentCompany.Change(companyId)</c> altında olmalıdır (kanal ürünü company-owned).</summary>
    public async Task<SalesChannelTrTrendyolProductDto> SeedAsync(
        Guid companyId, string productCode, bool verify, bool seedSkus, int? safetyStock = null, decimal? minPrice = null)
    {
        SalesChannelTrTrendyol channel;
        Product product;
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            channel = await _channelRepository.InsertAsync(
                new SalesChannelTrTrendyol(companyId, $"TY-{productCode}", $"Trendyol {productCode}", "seller-1", "api-key", "api-secret"),
                autoSave: true);
            product = await _productRepository.InsertAsync(new Product(companyId, productCode, $"Urun {productCode}"), autoSave: true);
            await uow.CompleteAsync();
        }

        await SeedErpVariantsAsync(companyId, product, verify, ("Red", 100m, 10), ("Blue", 100m, 20));

        var created = await _appService.CreateAsync(new SalesChannelTrTrendyolProductCreateDto
        {
            ProductId = product.Id,
            SalesChannelId = channel.Id,
            CategoryId = "411",
            BrandId = "1",
            VatRate = 20,
            SafetyStock = safetyStock,
            MinPrice = minPrice,
        });

        if (seedSkus)
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true);
            var entity = await _channelProductRepository.GetAsync(created.Id);
            var variants = await _erpVariantRepository.GetListAsync(
                v => v.EntityName == ProductEntityName && v.EntityId == product.Id);
            foreach (var v in variants)
            {
                entity.UpsertImportedSku(v.Id, $"BC-{productCode}-{v.Code}", v.Code, remoteContentId: 1);
            }

            await _channelProductRepository.UpdateAsync(entity, autoSave: true);
            await uow.CompleteAsync();
        }

        return created;
    }

    /// <summary>Ürüne "Renk" niteliği + verilen değerleri ekler, ERP varyantlarını senkronlar, her varyanta stok +
    /// satış fiyatı (+ istenirse <c>VerifiedRecipeStamp</c>) yazar.</summary>
    public async Task SeedErpVariantsAsync(
        Guid companyId, Product product, bool verify, params (string Value, decimal Price, int Stock)[] values)
    {
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var attribute = await _erpAttributeRepository.InsertAsync(
                new EntityAttribute(companyId, ProductEntityName, product.Id, "Renk", 0), autoSave: true);
            for (var i = 0; i < values.Length; i++)
            {
                await _erpValueRepository.InsertAsync(
                    new EntityAttributeValue(companyId, attribute.Id, values[i].Value, i), autoSave: true);
            }

            await _erpSynchronizer.SynchronizeAsync(ProductEntityName, product.Id, companyId, product.Name);
            await uow.CompleteAsync();
        }

        using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var variants = await _erpVariantRepository.GetListAsync(
                v => v.EntityName == ProductEntityName && v.EntityId == product.Id);
            foreach (var (value, price, stock) in values)
            {
                var variant = variants.Single(v => v.Code == value.ToUpperInvariant());
                variant.SetStock(stock);
                await _erpVariantRepository.UpdateAsync(variant, autoSave: true);

                var detail = new ProductVariantDetail(companyId, variant.Id);
                detail.SetSalePrice(price, null);

                // PUSH GUARD'I (§6): varyant aday listesine ancak İNSAN onayıyla girer. verify=false olan
                // fikstür tam da bu guard'ı sınamak içindir — stamp BASILMAZ.
                if (verify)
                {
                    detail.MarkVerified(RecipeVerificationStamp.EmptyRecipe, DateTime.UtcNow, verifiedBy: null);
                }

                await _variantDetailRepository.InsertAsync(detail, autoSave: true);
            }

            await uow.CompleteAsync();
        }
    }
}
