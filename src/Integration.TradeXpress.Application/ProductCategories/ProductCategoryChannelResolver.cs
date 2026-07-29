using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Bir çekirdek kategorinin bir KANALDAKİ karşılığını ve o karşılığın KOMİSYON oranını çözer.
///
/// <para><b>Eşleştirme KALITIMLIDIR</b> — nitelik kalıtımıyla aynı mantık: kategorinin kendi eşleştirmesi yoksa
/// ata zinciri yukarı yürünür ve ilk bulunan eşleştirme kullanılır. Sebep pratik: kullanıcı "Takı" düzeyinde bir
/// kez eşleştirir, altındaki onlarca kategori otomatik çözülür; yalnız farklı davranması gerekenler kendi
/// eşleştirmesini tanımlar (en dar tanım kazanır).</para>
///
/// <para><b>Fail-soft:</b> eşleştirme yoksa, kanal kategorisi taksonomiden bulunamazsa ya da komisyon oranı boşsa
/// <c>null</c> döner — çağıran (yan-maliyet planı) komisyon satırı üretmez. Fail-fast OLMAZ: kategori eşleştirmesi
/// olmadan da ürün kaydedilebilmeli, fiyat hesaplanabilmelidir.</para>
/// </summary>
public class ProductCategoryChannelResolver : ITransientDependency
{
    private readonly IRepository<ProductCategory, Guid> _categoryRepository;
    private readonly IRepository<ProductCategoryChannelMapping, Guid> _mappingRepository;
    private readonly IRepository<N11Category, Guid> _n11CategoryRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public ProductCategoryChannelResolver(
        IRepository<ProductCategory, Guid> categoryRepository,
        IRepository<ProductCategoryChannelMapping, Guid> mappingRepository,
        IRepository<N11Category, Guid> n11CategoryRepository,
        ICurrentTenant currentTenant,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _categoryRepository = categoryRepository;
        _mappingRepository = mappingRepository;
        _n11CategoryRepository = n11CategoryRepository;
        _currentTenant = currentTenant;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>
    /// Kategorinin (ya da en yakın atasının) kanal karşılığını döndürür — yoksa <c>null</c>.
    /// </summary>
    public virtual async Task<ProductCategoryChannelMapping?> ResolveMappingAsync(
        Guid companyId,
        Guid productCategoryId,
        SalesChannelType channel)
    {
        if (productCategoryId == Guid.Empty)
        {
            return null;
        }

        var chain = await LoadSelfAndAncestorsAsync(companyId, productCategoryId);
        if (chain.Count == 0)
        {
            return null;
        }

        var chainIds = chain.Select(c => c.Id).ToList();
        var mappings = await _asyncExecuter.ToListAsync(
            (await _mappingRepository.GetQueryableAsync())
                .Where(m => m.CompanyId == companyId
                    && m.Channel == channel
                    && chainIds.Contains(m.ProductCategoryId)));

        if (mappings.Count == 0)
        {
            return null;
        }

        // EN YAKIN eşleştirme kazanır: zincir kategoriden köke doğru sıralıdır, ilk eşleşen en dardır.
        var byCategory = mappings.ToDictionary(m => m.ProductCategoryId);
        foreach (var category in chain)
        {
            if (byCategory.TryGetValue(category.Id, out var mapping))
            {
                return mapping;
            }
        }

        return null;
    }

    /// <summary>
    /// Kategoriden çözülen EFEKTİF komisyon oranı (kategori komisyonu + kanalın zorunlu hizmet bedelleri,
    /// KDV brütüyle) — SSOT <see cref="N11CategoryCommissionImporter.ResolveEffectiveCommissionRate"/>.
    /// Bugün yalnız N11'in kategori bazlı komisyonu vardır; diğer kanallarda <c>null</c> döner ve çağıran
    /// kanal varsayılanına düşer.
    /// </summary>
    public virtual async Task<decimal?> ResolveCommissionRateAsync(
        Guid companyId,
        Guid productCategoryId,
        SalesChannelType channel,
        decimal? channelDefaultRate)
    {
        if (channel != SalesChannelType.TrN11)
        {
            // Trendyol/Etsy taksonomilerinde kategori komisyonu TUTULMUYOR (yalnız kanal varsayılanı vardır).
            return null;
        }

        var mapping = await ResolveMappingAsync(companyId, productCategoryId, channel);
        if (mapping is null)
        {
            return null;
        }

        // Kanal taksonomisi HOST-GLOBAL (tenant filtresi kapatılır — N11CategoryAppService ile aynı sınır).
        N11Category? channelCategory;
        using (_currentTenant.Change(null))
        {
            channelCategory = await _asyncExecuter.FirstOrDefaultAsync(
                (await _n11CategoryRepository.GetQueryableAsync())
                    .Where(c => c.ExternalId == mapping.ChannelCategoryExternalId));
        }

        if (channelCategory is null)
        {
            // Taksonomi yeniden senkronlandığında eşleştirme çözümlenemez hâle gelebilir — sessizce boş dön
            // (komisyon satırı üretilmez), eşleştirmeyi silme: kullanıcı kanal kategorisini yeniden seçsin.
            return null;
        }

        return N11CategoryCommissionImporter.ResolveEffectiveCommissionRate(
            channelCategory.CommissionRate,
            channelCategory.MarketingFeeRate,
            channelCategory.MarketplaceFeeRate,
            channelDefaultRate);
    }

    /// <summary>
    /// Kategoriden başlayıp köke kadar zincir (kategori İLK, kök SON). Ziyaret işareti bozuk veride oluşmuş bir
    /// döngüde yürüyüşü keser. Şirket filtresi her adımda: başka şirketin ağacına atlanamaz.
    /// </summary>
    private async Task<List<ProductCategory>> LoadSelfAndAncestorsAsync(Guid companyId, Guid startId)
    {
        var chain = new List<ProductCategory>();
        var visited = new HashSet<Guid>();
        var current = startId;

        while (current != Guid.Empty && visited.Add(current))
        {
            var node = await _categoryRepository.FindAsync(x => x.Id == current && x.CompanyId == companyId);
            if (node is null)
            {
                break;   // fail-soft: silinmiş/başka şirkete ait kategori → zincir olduğu yerde biter
            }

            chain.Add(node);
            current = node.ParentId ?? Guid.Empty;
        }

        return chain;
    }
}
