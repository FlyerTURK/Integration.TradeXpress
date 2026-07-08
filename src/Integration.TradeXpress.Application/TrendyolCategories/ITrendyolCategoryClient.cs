using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Trendyol;

namespace Integration.TradeXpress.TrendyolCategories;

/// <summary>
/// Trendyol kategori API istemcisi (server-side infra) — REST/JSON. Tüm ağaç tek çağrıda gelir
/// (<c>GET /integration/product/product-categories</c> → iç içe <c>subCategories</c>); burada FLAT tek şekle indirilir.
/// Endpoint <b>public</b> (auth gerektirmez) ama zorunlu <c>User-Agent</c> için SellerId lazım → kimlik yine de geçilir
/// (tutarlılık + T2/T3 desteği). <see cref="N11Categories.IN11CategoryClient"/> sözleşme şekliyle simetrik.
/// </summary>
public interface ITrendyolCategoryClient
{
    /// <summary>Tüm kategori ağacını FLAT liste olarak çeker (parent id ile bağlanır; <c>subCategories</c> boş = yaprak).</summary>
    Task<IReadOnlyList<TrendyolCategoryNode>> GetCategoryTreeAsync(
        TrendyolCredentials credentials, CancellationToken cancellationToken = default);
}

/// <summary>Normalize kategori düğümü (iç içe ağaçtan düzleştirilmiş). Id-only ağaç: parent id ile bağlanır.</summary>
public sealed record TrendyolCategoryNode(string ExternalId, string? ParentExternalId, string Name, bool IsLeaf);
