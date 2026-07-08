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

    /// <summary>Bir YAPRAK kategorinin attribute+value tanımlarını çeker
    /// (<c>GET /integration/product/product-categories/{categoryId}/attributes</c>). Değerler id-bazlı (<c>{id,name}</c>).
    /// Auth ZORUNLU (Basic + zorunlu User-Agent — taban halleder). <see cref="N11Categories.IN11CategoryClient.GetLeafAttributesAsync"/>
    /// ile simetrik ama id-bazlı (N11 name/value, Trendyol attributeValueId).</summary>
    Task<TrendyolLeafAttributes> GetLeafAttributesAsync(
        TrendyolCredentials credentials, string categoryExternalId, CancellationToken cancellationToken = default);
}

/// <summary>Normalize kategori düğümü (iç içe ağaçtan düzleştirilmiş). Id-only ağaç: parent id ile bağlanır.</summary>
public sealed record TrendyolCategoryNode(string ExternalId, string? ParentExternalId, string Name, bool IsLeaf);

/// <summary>Bir yaprak kategorinin attribute seti (on-demand; kalıcı SAKLANMAZ, yalnız cache'lenir).</summary>
public sealed record TrendyolLeafAttributes(string ExternalId, IReadOnlyList<TrendyolAttributeDef> Attributes);

/// <summary>Kategori attribute tanımı — id-bazlı. <see cref="Required"/>=zorunlu; <see cref="Varianter"/>=varyant ekseni
/// (SKU başına — ürün seviyesinde göstermelik değil); <see cref="AllowCustom"/>=serbest metin (customValue) izinli.
/// <see cref="Values"/> boş = serbest değer beklenir.</summary>
public sealed record TrendyolAttributeDef(
    int AttributeId, string Name, bool Required, bool Varianter, bool AllowCustom, IReadOnlyList<TrendyolAttributeValue> Values);

/// <summary>Attribute value — id-bazlı; seçilince <c>AttributeValueId</c> olarak yazılır (N11'in valueId'siyle simetrik).</summary>
public sealed record TrendyolAttributeValue(int ValueId, string Value);
