using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// N11 sentetik mega üst-katmanını uygular (IDEMPOTENT): <see cref="N11MegaCategories.Megas"/> 9 mega node'unu
/// eksikse ekler + <see cref="N11MegaCategories.TopToMega"/> haritasına göre 79 top-level'ı meganın altına bağlar.
/// HOST-GLOBAL yazım → <c>CurrentTenant.Change(null)</c> (db-per-tenant merkeziliği). Hem <c>SyncCategoriesAsync</c>
/// sonunda (re-sync 79'u parent=null'a çektiğinden yeniden bağlamak için) hem seeder'da (uygulama başlangıcı) çağrılır.
/// </summary>
public class N11CategoryMegaGrouper : DomainService
{
    private readonly IRepository<N11Category, Guid> _repository;

    public N11CategoryMegaGrouper(IRepository<N11Category, Guid> repository)
    {
        _repository = repository;
    }

    public virtual async Task EnsureAsync()
    {
        using (CurrentTenant.Change(null))
        {
            var all = await _repository.GetListAsync();
            var byExternalId = all.ToDictionary(x => x.ExternalId, StringComparer.Ordinal);

            await EnsureMegaNodesAsync(byExternalId);
            await ReparentTopCategoriesAsync(byExternalId);
        }
    }

    /// <summary>9 mega node'u yoksa ekler (yeni kökler: parent=null, IsLeaf=false).</summary>
    private async Task EnsureMegaNodesAsync(Dictionary<string, N11Category> byExternalId)
    {
        var newMegas = new List<N11Category>();
        foreach (var (externalId, name) in N11MegaCategories.Megas)
        {
            if (!byExternalId.ContainsKey(externalId))
            {
                var mega = new N11Category(externalId, null, name, isLeaf: false, lastModifiedExternal: null);
                newMegas.Add(mega);
                byExternalId[externalId] = mega;
            }
        }

        if (newMegas.Count > 0)
        {
            await _repository.InsertManyAsync(newMegas, autoSave: true);
        }
    }

    /// <summary>79 top-level'ı map'e göre meganın altına bağlar (parent zaten doğruysa dokunmaz). Haritada olmayan
    /// bir top varsa (N11 yeni kök eklerse) kök bırakılır + uyarı loglanır (sessiz düşmez).</summary>
    private async Task ReparentTopCategoriesAsync(Dictionary<string, N11Category> byExternalId)
    {
        var toUpdate = new List<N11Category>();
        foreach (var (topExternalId, megaExternalId) in N11MegaCategories.TopToMega)
        {
            if (byExternalId.TryGetValue(topExternalId, out var top)
                && !string.Equals(top.ParentExternalId, megaExternalId, StringComparison.Ordinal))
            {
                top.SetParent(megaExternalId);
                toUpdate.Add(top);
            }
        }

        if (toUpdate.Count > 0)
        {
            await _repository.UpdateManyAsync(toUpdate, autoSave: true);
        }

        // Haritalanmamış top-level (N11'in yeni kökü) varsa raporla — eşleme güncellenmeli.
        var unmapped = byExternalId.Values
            .Where(c => c.ParentExternalId is null
                && !N11MegaCategories.TopToMega.ContainsKey(c.ExternalId)
                && !N11MegaCategories.Megas.Any(m => m.ExternalId == c.ExternalId))
            .ToList();
        if (unmapped.Count > 0)
        {
            Logger.LogWarning(
                "N11 mega eşlemesi eksik: {Count} top-level kategori kök kaldı → {Ids}",
                unmapped.Count,
                string.Join(", ", unmapped.Select(c => $"{c.ExternalId}({c.Name})")));
        }
    }
}
