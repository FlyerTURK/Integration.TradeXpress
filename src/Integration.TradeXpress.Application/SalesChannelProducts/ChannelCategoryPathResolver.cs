using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EtsyTaxonomies;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolCategories;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.SalesChannelProducts;

/// <summary>Kategori ağacının TEK düğümü — üç pazaryerinin ortak tipi (id · üst id · ad).</summary>
public sealed record ChannelCategoryNode(string ExternalId, string? ParentExternalId, string Name);

/// <summary>
/// Kanal kategorisinin KÖKTEN TAM YOLU ("Kozmetik &gt; Cilt Bakımı &gt; Göz Makyaj Temizleyici").
///
/// <para><b>Neden yaprak adı yetmiyor</b> (2026-08-10 Hakan): kanal-ürün kaydı yalnız YAPRAĞIN adını
/// dondurur ve yaprak adları ağaç içinde benzersiz DEĞİLDİR — "Bileklik" hem takıda hem saat aksesuarında,
/// "Aksesuar" onlarca dalda geçer. Tek başına yaprak adı hangi dalda olduğunu söylemez; komisyon oranı
/// (<c>TrendyolCommissionResolver</c>) ve zorunlu öznitelikler tam da o dala bağlı olduğundan, yanlış dal
/// yanlış fiyat demektir. Yol, kararı gözle doğrulanabilir kılar.</para>
///
/// <para><b>Neden ÜÇÜ İÇİN TEK sınıf:</b> N11 · Trendyol · Etsy ağaçları ayrı tablolardır ama şekilleri
/// HARFİ HARFİNE aynıdır (<c>ExternalId</c> · <c>ParentExternalId</c> · <c>Name</c>) ve yürüme algoritması
/// tektir. Üç kopya, döngü guard'ı ya da ayraç değiştiğinde ikisinin sessizce eski kalması demekti.</para>
///
/// <para><b>Neden ağacın TAMAMI yüklenmiyor:</b> Trendyol ağacı ~3.300 düğüm. Liste her açılışta tümünü
/// çekseydi, gösterilen ~10² satır için binlerce satır okunurdu. Bunun yerine ATA ZİNCİRİ dalga dalga
/// yürünür: önce yapraklar, sonra onların üstleri… Derinlik pratikte 3-5 olduğundan tur sayısı da o kadar;
/// her tur yalnız o seviyedeki düğümleri okur (<c>IN (...)</c>). Sığ ağaçta bu, tam yüklemeden bir
/// büyüklük mertebesi ucuzdur.</para>
///
/// <para><b>Bayat id SESSİZ ATLANIR, uydurulmaz:</b> kanal kaydındaki kategori id'si ağaçta yoksa (pazaryeri
/// kategoriyi kaldırmış/yeniden numaralandırmış) o id sözlükte YER ALMAZ. Çağıran, kaydın dondurduğu yaprak
/// adına düşer — "yol çözülemedi" diye boş bırakmak, elde duran doğru bilgiyi çöpe atmak olurdu.</para>
/// </summary>
public class ChannelCategoryPathResolver : ITransientDependency
{
    /// <summary>Yol ayracı — ekranda ve aramada aynı. Tek yerde durur ki üç kanalda da aynı görünsün.</summary>
    public const string Separator = " > ";

    /// <summary>Hem tur sayısının hem yol uzunluğunun tavanı. Bozuk veri (kendi kendinin ebeveyni olan düğüm,
    /// halka) sonsuz döngüye çevrilmesin diye — ağaçların gerçek derinliği 3-5.</summary>
    private const int MaxDepth = 20;

    private readonly IRepository<N11Category, Guid> _n11CategoryRepository;
    private readonly IRepository<TrendyolCategory, Guid> _trendyolCategoryRepository;
    private readonly IRepository<EtsyTaxonomy, Guid> _etsyTaxonomyRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public ChannelCategoryPathResolver(
        IRepository<N11Category, Guid> n11CategoryRepository,
        IRepository<TrendyolCategory, Guid> trendyolCategoryRepository,
        IRepository<EtsyTaxonomy, Guid> etsyTaxonomyRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _n11CategoryRepository = n11CategoryRepository;
        _trendyolCategoryRepository = trendyolCategoryRepository;
        _etsyTaxonomyRepository = etsyTaxonomyRepository;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>Verilen yaprak id'leri için tam yol sözlüğü. Çözülemeyen id sözlükte YOKTUR (bkz. sınıf özeti).</summary>
    public virtual async Task<Dictionary<string, string>> ResolveAsync(
        SalesChannelType channelType,
        IEnumerable<string> leafExternalIds)
    {
        var wanted = leafExternalIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (wanted.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var known = await LoadAncestorChainAsync(channelType, wanted);

        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in wanted)
        {
            if (known.TryGetValue(id, out var leaf))
            {
                paths[id] = BuildPath(leaf, known);
            }
        }

        return paths;
    }

    /// <summary>Yapraklardan köke doğru DALGA DALGA yükleme — her tur yalnız bir sonraki seviyeyi okur.</summary>
    private async Task<Dictionary<string, ChannelCategoryNode>> LoadAncestorChainAsync(
        SalesChannelType channelType,
        HashSet<string> leafExternalIds)
    {
        var known = new Dictionary<string, ChannelCategoryNode>(StringComparer.Ordinal);
        var pending = new HashSet<string>(leafExternalIds, StringComparer.Ordinal);
        var depth = 0;

        while (pending.Count > 0 && depth < MaxDepth)
        {
            depth++;
            var loaded = await LoadNodesAsync(channelType, pending);
            pending = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in loaded)
            {
                known[node.ExternalId] = node;
            }

            // Bir sonraki tur = HENÜZ BİLİNMEYEN üstler. "Bilinen"i dışlamak halkayı da kapatır:
            // A→B→A durumunda B'nin üstü A zaten okunmuştur, tur biter.
            foreach (var node in loaded)
            {
                if (node.ParentExternalId is { } parentId
                    && !string.IsNullOrWhiteSpace(parentId)
                    && !known.ContainsKey(parentId))
                {
                    pending.Add(parentId);
                }
            }
        }

        return known;
    }

    private async Task<List<ChannelCategoryNode>> LoadNodesAsync(
        SalesChannelType channelType,
        HashSet<string> externalIds)
    {
        // EF'in IN (...) çevirisi için somut liste (HashSet üzerinden Contains çevrilmez).
        var ids = externalIds.ToList();

        switch (channelType)
        {
            case SalesChannelType.TrN11:
            {
                var query = (await _n11CategoryRepository.GetQueryableAsync())
                    .Where(c => ids.Contains(c.ExternalId))
                    .Select(c => new ChannelCategoryNode(c.ExternalId, c.ParentExternalId, c.Name));
                return await _asyncExecuter.ToListAsync(query);
            }

            case SalesChannelType.TrTrendyol:
            {
                var query = (await _trendyolCategoryRepository.GetQueryableAsync())
                    .Where(c => ids.Contains(c.ExternalId))
                    .Select(c => new ChannelCategoryNode(c.ExternalId, c.ParentExternalId, c.Name));
                return await _asyncExecuter.ToListAsync(query);
            }

            case SalesChannelType.Etsy:
            {
                var query = (await _etsyTaxonomyRepository.GetQueryableAsync())
                    .Where(c => ids.Contains(c.ExternalId))
                    .Select(c => new ChannelCategoryNode(c.ExternalId, c.ParentExternalId, c.Name));
                return await _asyncExecuter.ToListAsync(query);
            }

            default:
            {
                // Ağacı OLMAYAN kanal tipi → boş liste. Burada patlamak, yeni bir kanal eklendiğinde
                // ilgisiz bir listeyi düşürürdü; yol yoksa çağıran zaten yaprak adına düşer.
                return new List<ChannelCategoryNode>();
            }
        }
    }

    private static string BuildPath(ChannelCategoryNode leaf, Dictionary<string, ChannelCategoryNode> known)
    {
        var parts = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = leaf;

        while (current is not null && visited.Add(current.ExternalId) && parts.Count < MaxDepth)
        {
            parts.Add(current.Name);
            current = current.ParentExternalId is { } parentId && known.TryGetValue(parentId, out var parent)
                ? parent
                : null;
        }

        parts.Reverse();
        return string.Join(Separator, parts);
    }
}
