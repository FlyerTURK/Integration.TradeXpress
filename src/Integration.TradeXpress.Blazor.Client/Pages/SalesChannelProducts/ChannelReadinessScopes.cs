using System;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannelProducts;

/// <summary>
/// KANAL FORMLARININ SATIŞA-HAZIRLIK KAPSAM YOLLARI — tek yer (2026-08-19).
///
/// <para><b>Neden ayrı sınıf:</b> "kanal ürünü kaydedilmemişse kapsam yoktur" ve "ERP karşılığı olmayan
/// (kanal-özel) satırın core varyantı yoktur → kapsam yoktur" kuralları bir KURAL'dır, markup detayı değil.
/// N11 ve Trendyol formları birebir aynı işi yaptığı için bu kural iki code-behind'a kopyalanmıştı; kopyalanan
/// kural bir yerde güncellenip diğerinde unutulur ve tam olarak "işaret görünmüyor" biçiminde, yani sessizce
/// bozulur. Etsy formu da işaretlendiğinde ÜÇÜNCÜ kopya doğardı.</para>
///
/// <para><b>Boş kimlik = <c>null</c> kapsam:</b> kaydedilmemiş kaydın kimliği yoktur, dolayısıyla hakkında issue
/// da olamaz. <c>null</c> kapsamı "kök" saymak, ürünün TÜM issue'larını o satıra/sekmeye yapıştırırdı — formlar
/// bu yüzden <c>TreatNullScopeAsRoot="false"</c> ile çizer.</para>
/// </summary>
public static class ChannelReadinessScopes
{
    /// <summary>Kanal ürününün kendisi: <c>channels/{id}</c>. Kimlik yoksa kapsam da yok.</summary>
    public static string? ChannelOf(Guid channelProductId)
    {
        if (channelProductId == Guid.Empty)
        {
            return null;
        }

        return SaleReadinessScope.Channel(channelProductId);
    }

    /// <summary>Kanal ürününün kombinasyon/varyant sekmesi: <c>channels/{id}/variants</c>.</summary>
    public static string? VariantsOf(Guid channelProductId)
    {
        if (channelProductId == Guid.Empty)
        {
            return null;
        }

        return SaleReadinessScope.ChannelVariants(channelProductId);
    }

    /// <summary>Bir kombinasyon SATIRI. Kimlik CORE varyantındır; kanal-özel (ERP karşılığı olmayan) satırda
    /// böyle bir kimlik yoktur → kapsam yoktur, işaret çizilmez.</summary>
    public static string? VariantOf(Guid channelProductId, Guid? productVariantId)
    {
        if (!TryResolve(channelProductId, productVariantId, out var variantId))
        {
            return null;
        }

        return SaleReadinessScope.ChannelVariant(channelProductId, variantId);
    }

    /// <summary>Kombinasyon satırının reçete/emtia bölümü — "temel emtia eklenmedi" issue'sunun en derin yeri.</summary>
    public static string? VariantRecipeOf(Guid channelProductId, Guid? productVariantId)
    {
        if (!TryResolve(channelProductId, productVariantId, out var variantId))
        {
            return null;
        }

        return SaleReadinessScope.ChannelVariantRecipe(channelProductId, variantId);
    }

    // İki kimliğin de DOLU olması şartı — tek yerde, iki çağıran da aynı sınırı görsün diye.
    private static bool TryResolve(Guid channelProductId, Guid? productVariantId, out Guid variantId)
    {
        variantId = Guid.Empty;
        if (channelProductId == Guid.Empty)
        {
            return false;
        }

        if (productVariantId is not { } candidate || candidate == Guid.Empty)
        {
            return false;
        }

        variantId = candidate;
        return true;
    }
}
