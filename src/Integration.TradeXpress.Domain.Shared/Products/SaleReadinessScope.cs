using System;

namespace Integration.TradeXpress.Products;

/// <summary>
/// SATIŞA-HAZIRLIK ISSUE'UNUN KAPSAM YOLU (2026-08-19 Hakan kuralı: <i>"hem Kanal ürünleri tabı, hem hangi üründe
/// bu yapılmadı ise, hem satış kanalı ürününde varyantlar tabı, hem varyant, hem de varyant emtiaları tabı bu
/// hataya bizi yönlendirmeli"</i>).
///
/// <para><b>Neden yol:</b> bir issue tek bir yerde değil, İÇİNDE bulunduğu her seviyede görünmeli. Bunu "hangi
/// sekme hangi issue'yu gösterir" diye UI'da kurallaştırmak, kuralı ikinci kez (ve eksik) yazmak olurdu. Bunun
/// yerine sunucu issue'ya <b>hiyerarşik yol</b> verir (<c>channels/{id}/variants/{id}/recipe</c>); her panel/sekme
/// "benim yolumla başlayan issue'ların EN YÜKSEK ağırlığı ne?" diye sorar ve ona göre renklenir. Yeni bir seviye
/// eklendiğinde UI'da kural değişmez, yalnız yeni bir yol parçası doğar.</para>
///
/// <para><b>Biçim:</b> <c>/</c> ile ayrılmış küçük harfli segmentler; kimlikli seviyelerde <c>{ad}:{guid}</c>.
/// Ön-ek kıyası yapılacağı için segment sınırına dikkat edilir (<c>variants</c> yolu <c>variantsX</c>'i
/// EŞLEŞTİRMEZ — <see cref="IsWithin"/> segment sınırını gözetir).</para>
/// </summary>
public static class SaleReadinessScope
{
    public const string Separator = "/";

    /// <summary>Ürün geneli — sekme dışı alanlar (kategori, KDV, durum, stok politikası).</summary>
    public const string General = "general";

    /// <summary>Ürün formu → Varyantlar sekmesi.</summary>
    public const string Variants = "variants";

    /// <summary>Ürün formu → Medya sekmesi.</summary>
    public const string Media = "media";

    /// <summary>Satışa doğrulama (satışa hazırlık paneli düğmesi + varyant statüleri).</summary>
    public const string Verification = "verification";

    /// <summary>Ürün formu → Satış Kanalı Ürünleri sekmesi.</summary>
    public const string Channels = "channels";

    /// <summary>Bir varyantın REÇETE/EMTİA bölümü — "temel emtia eklenmedi" issue'unun en derin yeri.</summary>
    public const string Recipe = "recipe";

    /// <summary>Belirli bir core varyant: <c>variants/{id}</c>.</summary>
    public static string Variant(Guid variantId)
    {
        return Variants + Separator + variantId;
    }

    /// <summary>Bir varyantın reçetesi: <c>variants/{id}/recipe</c>.</summary>
    public static string VariantRecipe(Guid variantId)
    {
        return Variant(variantId) + Separator + Recipe;
    }

    /// <summary>Belirli bir kanal ürünü: <c>channels/{id}</c>.</summary>
    public static string Channel(Guid channelProductId)
    {
        return Channels + Separator + channelProductId;
    }

    /// <summary>Kanal ürününün varyant/kombinasyon sekmesi: <c>channels/{id}/variants</c>.</summary>
    public static string ChannelVariants(Guid channelProductId)
    {
        return Channel(channelProductId) + Separator + Variants;
    }

    /// <summary>Kanal ürününün BİR varyant satırı: <c>channels/{kanalÜrünü}/variants/{coreVaryant}</c>.
    /// Kimlik CORE varyantındır (kanal satırı ona bağlıdır) — böylece aynı varyantın core ve kanal
    /// yolları aynı kimliği taşır ve iki panel aynı issue'yu gösterir.</summary>
    public static string ChannelVariant(Guid channelProductId, Guid variantId)
    {
        return ChannelVariants(channelProductId) + Separator + variantId;
    }

    /// <summary>Kanal varyant satırının reçete/emtia bölümü.</summary>
    public static string ChannelVariantRecipe(Guid channelProductId, Guid variantId)
    {
        return ChannelVariant(channelProductId, variantId) + Separator + Recipe;
    }

    /// <summary><paramref name="path"/> issue'u <paramref name="scope"/> kapsamının İÇİNDE mi (kapsamın kendisi
    /// ya da altı). Segment sınırı gözetilir: <c>variants</c> kapsamı <c>variants/{id}</c>'yi kapsar ama
    /// <c>variantsummary</c>'yi KAPSAMAZ. Boş kapsam = kök (her issue içindedir).</summary>
    public static bool IsWithin(string? path, string? scope)
    {
        if (string.IsNullOrEmpty(scope))
        {
            return true;
        }

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (!path.StartsWith(scope, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Length == scope.Length
               || path[scope.Length].ToString() == Separator;
    }
}
