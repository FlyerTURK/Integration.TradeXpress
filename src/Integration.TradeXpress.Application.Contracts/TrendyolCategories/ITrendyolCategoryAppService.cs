using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.TrendyolCategories;

/// <summary>
/// Trendyol kategori taksonomisi — host-global ağaç. Ağaç, çalışılan şirketin Trendyol kanalının kimliğiyle
/// (zorunlu User-Agent için SellerId lazım; endpoint public) bir kez sync'lenir ve tüm tenant'lar paylaşır.
/// <see cref="N11Categories.IN11CategoryAppService"/> ile simetrik (attribute/on-demand katmanı T2 dilimine ertelendi).
/// </summary>
public interface ITrendyolCategoryAppService : IApplicationService
{
    /// <summary>REST'ten tüm kategori ağacını çekip <c>TrendyolCategory</c>'ye upsert eder (host-global). Eklenen+güncellenen sayısını döner.</summary>
    Task<int> SyncCategoriesAsync();

    /// <summary>Ağaç gezinme — verilen üst kategorinin çocukları (null → kök kategoriler). Host-global veriden okunur.</summary>
    Task<List<TrendyolCategoryTreeNodeDto>> GetChildrenAsync(string? parentExternalId);

    /// <summary>Yaprak kategori SERVER-SIDE arama (LookupEdit): en az 3 harf; Türkçe aksan/case-duyarsız ("kul"→"Kül").
    /// Tam yol adıyla döner (yaprak adları tekrar ettiğinden), en fazla 50 sonuç. &lt;3 harf → boş.</summary>
    Task<List<TrendyolLeafCategoryDto>> SearchLeafCategoriesAsync(string term);
}
