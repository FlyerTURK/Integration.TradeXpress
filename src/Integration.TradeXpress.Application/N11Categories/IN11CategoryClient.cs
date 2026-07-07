using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// N11 kategori API istemcisi (server-side infra) — <b>REST primary, SOAP fallback</b> (keşif kararı 2026-07-06:
/// REST <c>/cdn</c> tüm ağacı tek çağrıda + valueId verir; SOAP yalnız erişilemezse devreye girer). İki servisin
/// FARKLI şekilleri burada tek normalize sözleşmeye indirilir. Çağıran kimlikleri (AppKey/AppSecret) parametre
/// geçer: kategori ağacı HOST kimliğiyle, attribute'lar ilgili SalesChannel'ın KENDİ kimliğiyle çekilir.
/// </summary>
public interface IN11CategoryClient
{
    /// <summary>Tüm kategori ağacını FLAT liste olarak çeker (REST <c>/cdn/categories</c> primary; SOAP walk fallback).</summary>
    Task<IReadOnlyList<N11CategoryNode>> GetCategoryTreeAsync(
        string appKey, string appSecret, CancellationToken cancellationToken = default);

    /// <summary>Bir YAPRAK kategorinin attribute+value'larını çeker (REST <c>/cdn/category/{id}/attribute</c> primary;
    /// SOAP <c>GetCategoryAttributes</c> fallback). <see cref="N11AttributeValue.ValueId"/> REST'te dolu, SOAP fallback'te null.</summary>
    Task<N11LeafAttributes> GetLeafAttributesAsync(
        string categoryExternalId, string appKey, string appSecret, CancellationToken cancellationToken = default);
}

/// <summary>Normalize kategori düğümü (REST tree veya SOAP walk'tan aynı şekil). Id-only ağaç: parent id ile bağlanır.</summary>
public sealed record N11CategoryNode(
    string ExternalId, string? ParentExternalId, string Name, bool IsLeaf, DateTime? LastModifiedExternal);

/// <summary>Bir yaprak kategorinin attribute seti (on-demand; saklanmaz).</summary>
public sealed record N11LeafAttributes(
    string ExternalId, string Name, IReadOnlyList<N11AttributeDef> Attributes);

/// <summary>Kategori attribute tanımı — zorunluluk/varyant/custom bayrakları + N11 öncelik sırası + value listesi.
/// <see cref="Priority"/> N11'in form sırası (WSDL: <c>xs:double</c>); çözülemezse null (yalnız GetCategoryAttributes
/// / REST taşır — özet GetCategoryAttributesId priority İÇERMEZ).</summary>
public sealed record N11AttributeDef(
    string AttributeId, string Name, bool IsMandatory, bool IsVariant, bool IsCustomValue, double? Priority, IReadOnlyList<N11AttributeValue> Values);

/// <summary>Attribute value — REST'te <see cref="ValueId"/> dolu (listelemede zorunlu); SOAP fallback'te null (yalnız ad).</summary>
public sealed record N11AttributeValue(string? ValueId, string Value);
