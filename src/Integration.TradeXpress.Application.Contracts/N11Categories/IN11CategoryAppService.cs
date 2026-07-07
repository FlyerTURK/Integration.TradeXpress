using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// N11 kategori taksonomisi — host-global ağaç + on-demand attribute. Ağaç HOST kimliğiyle bir kez sync'lenir
/// (tüm tenant'lar paylaşır); attribute'lar seçilen yaprak için ilgili SalesChannel'ın KENDİ kimliğiyle çekilir.
/// </summary>
public interface IN11CategoryAppService : IApplicationService
{
    /// <summary>Host-only: REST'ten tüm kategori ağacını çekip <c>N11Category</c>'ye upsert eder. Eklenen+güncellenen sayısını döner.</summary>
    Task<int> SyncCategoriesAsync();

    /// <summary>Ağaç gezinme — verilen üst kategorinin çocukları (null → 79 top). Host-global veriden okunur.</summary>
    Task<List<N11CategoryTreeNodeDto>> GetChildrenAsync(string? parentExternalId);

    /// <summary>Yaprak kategori SERVER-SIDE arama (LookupEdit): en az 3 harf; Türkçe aksan/case-duyarsız ("kul"→"Kül").
    /// Tam yol adıyla döner (yaprak adları tekrar ettiğinden), en fazla 50 sonuç. &lt;3 harf → boş.</summary>
    Task<List<N11LeafCategoryDto>> SearchLeafCategoriesAsync(string term);

    /// <summary>On-demand: bir YAPRAK kategorinin attribute+value'ları — çalışılan şirketin N11 kanalının kimliğiyle çekilir.</summary>
    Task<List<N11CategoryAttributeDto>> GetLeafAttributesAsync(string categoryExternalId);
}
