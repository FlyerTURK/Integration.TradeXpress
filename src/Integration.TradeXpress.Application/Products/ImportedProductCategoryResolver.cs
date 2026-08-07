using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Products;

/// <summary>
/// İçe aktarılan ürünün ÇEKİRDEK ürün kategorisini çözer/kurar (2026-08-06 Hakan kararı: <i>"madem sihirbaz
/// kullanıyoruz, o zaman sihirbaz kategorileri de otomatik olarak oluştursun"</i>).
///
/// <para><b>Boşluk neydi:</b> içe aktarım kanal kategorisini KANAL kaydına yazıyordu ama çekirdek
/// <c>Product.ProductCategoryId</c> hep boş kalıyordu — kullanıcı yüzlerce ürünü elle kategorilemek zorundaydı ve
/// çekirdek kategori ↔ kanal kategorisi eşlemesi (<see cref="ProductCategoryChannelMapping"/>) hiç dolmuyordu.</para>
///
/// <para><b>Kural:</b> eşleme (Company, Channel, ExternalId) anahtarıyla aranır; varsa çekirdek kategori ODUR.
/// Yoksa kanal kategorisinin ADIYLA kök seviyede bir çekirdek kategori bulunur-ya-da-açılır ve eşleme yazılır —
/// ikinci import aynı kategoriyi bulur, dublike üretmez. Ad çakışması meşru birleşmedir: "Kolye" adlı çekirdek
/// kategori zaten varsa yeni kanal kategorisi ona bağlanır.</para>
///
/// <para><b>Bilinçli sınır — DÜZ liste, ağaç değil:</b> pazaryeri ağacının tam yolunu ("Kozmetik &gt; Kadın
/// Hijyen &gt; …") çekirdek ağaca kopyalamak, kullanıcının kendi kategori düzenini pazaryerininkine teslim etmek
/// olurdu. Yaprak adıyla kök kategori açılır; kullanıcı isterse sonradan taşır (ağaç yönetimi zaten var).
/// Yalnız YENİ ürüne atanır — mevcut ürünün kategorisi kullanıcı beyanıdır, EZİLMEZ (minimal-güncelleme kuralı).</para>
/// </summary>
public class ImportedProductCategoryResolver : ITransientDependency
{
    private readonly IRepository<ProductCategory, Guid> _categoryRepository;
    private readonly IRepository<ProductCategoryChannelMapping, Guid> _mappingRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public ImportedProductCategoryResolver(
        IRepository<ProductCategory, Guid> categoryRepository,
        IRepository<ProductCategoryChannelMapping, Guid> mappingRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _categoryRepository = categoryRepository;
        _mappingRepository = mappingRepository;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>Kanal kategorisine karşılık gelen çekirdek kategori id'si — yoksa kurar. Kategori bilgisi hiç
    /// yoksa null (ürün kategorisiz kalır; import raporu eşleşmeyen kategoriyi zaten gösteriyor).</summary>
    public virtual async Task<Guid?> ResolveOrCreateAsync(
        Guid companyId, SalesChannelType channel, string? channelCategoryExternalId, string? channelCategoryName)
    {
        if (string.IsNullOrWhiteSpace(channelCategoryExternalId))
        {
            return null;
        }

        var externalId = channelCategoryExternalId.Trim();

        var mapping = await _asyncExecuter.FirstOrDefaultAsync(
            (await _mappingRepository.GetQueryableAsync())
                .Where(m => m.CompanyId == companyId
                            && m.Channel == channel
                            && m.ChannelCategoryExternalId == externalId));
        if (mapping is not null)
        {
            return mapping.ProductCategoryId;
        }

        var categoryId = await FindOrCreateRootCategoryAsync(companyId, channelCategoryName, externalId);

        var newMapping = new ProductCategoryChannelMapping(companyId, categoryId, channel, externalId);
        newMapping.SetChannelCategory(externalId, channelCategoryName?.Trim());
        await _mappingRepository.InsertAsync(newMapping, autoSave: true);

        return categoryId;
    }

    /// <summary>Kök seviyede adla bulur-ya-da-açar. Ad yoksa dış id kullanılır — adsız kategori sessizce
    /// atlanırsa eşleme hiç kurulamaz ve her import aynı boşluğa düşerdi.</summary>
    private async Task<Guid> FindOrCreateRootCategoryAsync(Guid companyId, string? name, string externalId)
    {
        var categoryName = string.IsNullOrWhiteSpace(name) ? externalId : name.Trim();

        var existing = await _asyncExecuter.FirstOrDefaultAsync(
            (await _categoryRepository.GetQueryableAsync())
                .Where(c => c.CompanyId == companyId && c.ParentId == null && c.Name == categoryName));
        if (existing is not null)
        {
            return existing.Id;
        }

        var category = new ProductCategory(companyId, categoryName);
        await _categoryRepository.InsertAsync(category, autoSave: true);
        return category.Id;
    }
}
