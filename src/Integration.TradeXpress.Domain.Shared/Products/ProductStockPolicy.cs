namespace Integration.TradeXpress.Products;

/// <summary>Kanal listelemesindeki stok sayısının KAYNAĞI (ADR-PRODUCT-ORCHESTRATION, 2026-07-25 Hakan kararı:
/// "ileride sabit ya da sınırsız stok özelliklerimiz de olacak").
/// <para><b>Sayısal düzen:</b> Fixed=0 bilinçli — STATÜKO (elle girilen stok); mevcut satırlar migration
/// default'u (0) ile davranış değiştirmez (default(enum) = statüko; ProductVariantMode deseniyle aynı).</para></summary>
public enum ProductStockPolicy
{
    /// <summary>Sabit — stok elle girilir (STATÜKO/varsayılan). Orkestratör bu ürünün stoğuna DOKUNMAZ.</summary>
    Fixed = 0,

    /// <summary>Hesaplı — satılabilir adet reçete + eldeki maden stoğundan türetilir (oversell koruması).
    /// Maden stoğu değişince orkestratör yeniden hesaplar; muadil ürünler doğal olarak bu politikadadır.</summary>
    Calculated = 1,

    /// <summary>Sınırsız — stok kısıtı yok; kanala daima "stokta var" gider (hizmet/sipariş-üzerine üretim).</summary>
    Unlimited = 2,
}
