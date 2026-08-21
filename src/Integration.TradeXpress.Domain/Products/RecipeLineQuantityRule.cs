namespace Integration.TradeXpress.Products;

/// <summary>
/// "ANA EMTİA 0 ADET / 0 MİKTAR GİRİLMEZ" (2026-08-19 Hakan kuralı: <i>"Ana emtialar 0 adet veya miktar olarak
/// girilmemeli"</i>). Katalog emtiası satırı (<see cref="RecipeComponentType.CatalogCommodity"/>) fiziksel bir şeyi
/// temsil eder; adedi de miktarı da sıfır olan satır hiçbir şey temsil etmez — maliyeti sıfırlar, stok zincirini
/// boş geçer, varyant "reçeteli" görünür ama reçete yoktur.
///
/// <para><b>Tek kaynak:</b> hem yazma yolu (<c>ProductRecipeLineWriter</c> — sıfır satırı reddeder) hem satışa
/// hazırlık validator'ı (<c>ProductSaleValidator</c> — mevcut kayıtlardaki sıfır satırı Error olarak işaretler) bu sınıfa
/// sorar. İki yerde ayrı yazılsaydı biri "adet VEYA miktar", diğeri "adet VE miktar" derdi.</para>
///
/// <para><b>Hizmet satırı kapsam dışı:</b> hizmetin adedi/miktarı yoktur (bedel <c>ManualAmount</c>/türev);
/// kural yalnız katalog emtiası içindir.</para>
/// </summary>
public static class RecipeLineQuantityRule
{
    /// <summary>Bu bileşen türü pozitif adet ya da miktar ister mi.</summary>
    public static bool RequiresPositiveQuantity(RecipeComponentType componentType)
    {
        return componentType == RecipeComponentType.CatalogCommodity;
    }

    /// <summary>Katalog emtiası için: adet ya da miktardan EN AZ BİRİ pozitif mi. Adetli emtiada miktar adetten
    /// türetilir, gramlı emtiada adet boş kalabilir — bu yüzden "ikisi birden" değil "en az biri".
    /// Kapsam dışı türlerde (hizmet) daima true.</summary>
    public static bool IsSatisfied(RecipeComponentType componentType, decimal quantity, decimal amount)
    {
        if (!RequiresPositiveQuantity(componentType))
        {
            return true;
        }

        return quantity > 0m || amount > 0m;
    }
}

/// <summary>
/// "KATALOG EMTİASI SATIRI EMTİASIZ OLAMAZ" (2026-08-21 ölçümü). <see cref="RecipeLineQuantityRule"/>'ın kardeşi
/// ve aynı dosyada: ikisi de bir <see cref="RecipeComponentType.CatalogCommodity"/> satırının VAR OLABİLMESİ için
/// gereken asgari koşulu söyler ve aynı iki çağıran sorar (yazma yolu <c>ProductRecipeLineWriter</c> +
/// satışa hazırlık <c>ProductSaleValidator</c>). Ayrı dosyaya koymak, "bu satır anlamlı mı" sorusunun
/// yarısını gözden kaçırmayı kolaylaştırırdı.
///
/// <para><b>Kapatılan delik:</b> <see cref="ProductVariantRecipeLine.CommodityId"/> nullable'dır (hizmet satırında
/// meşru şekilde boş) ve katalog satırında boş kalması HİÇBİR yerde reddedilmiyordu — kayıtta hata yok,
/// doğrulamada hata yok, push'ta hata yok. Sonuç SESSİZ YANLIŞ CEVAPTI: <c>ProductRecipeCostCalculator</c>
/// katalog kaydını bulamadığı için satırı maliyete katmaz, <c>RecipeCommodityIndex</c> satırı hiçbir emtiaya
/// bağlayamadığı için stok tetiği o ürünü uyandırmaz — varyant "reçeteli" görünürken maliyeti eksik,
/// satılabilir adedi yanlış çıkar. Sıfır adet/miktar satırıyla aynı sınıf hata, farklı alan.</para>
///
/// <para><b>Hizmet satırı kapsam dışı:</b> hizmette <c>CommodityId</c> yalnız ETİKET referansıdır (Service katalog
/// entity'sine dokunulmaz) ve boş bırakılabilir — türev satır bedelini üst satırlardan alır, emtiaya ihtiyaç
/// duymaz. Kapsamı genişletmek, bugün canlıda meşru şekilde emtiasız duran hizmet satırlarını kilitlerdi.</para>
/// </summary>
public static class RecipeLineCommodityRule
{
    /// <summary>Bu bileşen türü katalog kaydı referansı ister mi.</summary>
    public static bool RequiresCommodity(RecipeComponentType componentType)
    {
        return componentType == RecipeComponentType.CatalogCommodity;
    }

    /// <summary>Katalog emtiası için: satır bir katalog kaydına bağlı mı. <c>Guid.Empty</c> de boş sayılır —
    /// istemciden "seçilmedi" hâli null yerine boş Guid olarak gelebiliyor ve o değer hiçbir kaydı göstermez.
    /// Kapsam dışı türlerde (hizmet) daima true.</summary>
    public static bool IsSatisfied(RecipeComponentType componentType, Guid? commodityId)
    {
        if (!RequiresCommodity(componentType))
        {
            return true;
        }

        return commodityId is { } id && id != Guid.Empty;
    }
}
