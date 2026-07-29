namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>
/// Paket desisinin TEK çözümleme yeri: <b>varyantın kendi desisi, yoksa kanalın varsayılanı.</b>
///
/// <para>Kural neden tek yerde: desi kargo tarifesinin girdisidir ve tarife doğrudan satış fiyatına giriyor.
/// Kural iki-üç çağrı yerine kopyalanırsa biri güncellenip diğeri unutulur — aynı ürün iki ekranda farklı
/// fiyatlanır. (Aynı sebeple <c>ResolveEffectiveCommissionRate</c> de tek SSOT'tur.)</para>
///
/// <para>Saf ve DB'siz: girdiler dışarıdan verilir, test edilir.</para>
/// </summary>
public static class PackageDesiResolver
{
    /// <summary>
    /// Etkin desiyi çözer.
    /// </summary>
    /// <param name="variantPackageDesi">Varyantın kendi desisi (<c>ProductVariantDetail.PackageDesi</c>);
    /// <c>null</c> = varyanta özel bir değer girilmemiş.</param>
    /// <param name="channelDefaultPackageDesi">Kanalın varsayılan desisi
    /// (<c>SalesChannelBase.DefaultPackageDesi</c>).</param>
    /// <returns>Tarifeye girdi olacak desi. 0 geçerlidir — pazaryerinin "Dosya" basamağı.</returns>
    public static int Resolve(int? variantPackageDesi, int channelDefaultPackageDesi)
    {
        // Varyant değeri VARSA daima kazanır — 0 dahil. "?? " ile yazılırsa 0 da geçerli bir override olduğu
        // için doğru çalışır; ama niyeti açık bırakmak adına koşul yazılı: 0 "boş" DEĞİLDİR.
        if (variantPackageDesi is { } variantDesi)
        {
            return variantDesi < 0 ? 0 : variantDesi;
        }

        return channelDefaultPackageDesi < 0 ? 0 : channelDefaultPackageDesi;
    }
}
