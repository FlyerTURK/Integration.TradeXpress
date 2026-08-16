using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>
/// ÜRÜNDEN EMTİA TOHUMLAMA — aile → düzenleme formu eşlemesi ve tohum parametreleri, TEK yerde.
///
/// <para><b>Neden ortak:</b> aynı eşlemeyi iki yüzey soruyor — sihirbazın sınıflandırma paneli ve reçete
/// panelindeki "Üründen" anahtarı. İkinci kopya, sekizinci bir aile eklendiğinde birinin sessizce eski
/// kalması demekti (bu projede tam bu desen defalarca yaşandı).</para>
///
/// <para><b>Mamül AYRICALIKLI ve bu kasıtlı:</b> <c>Product</c> ile <c>Good</c> altyapıyı paylaşır (aynı
/// varyant sistemi, aynı medya bağlam çifti, aynı nitelik grafı) → tam projeksiyon taşınabilir. Diğer
/// ailelerde böyle bir paralellik YOKTUR (maden/taş milyem-karat semantiği olan hammaddedir); orada yalnız
/// kod/ad tohumlanır ve kalan zorunlu alanları (takip birimi gibi) kullanıcı doldurur. Uydurmak yerine
/// SORMAK, "sınıflandırma manueldir, yazılım tahmin etmez" kuralının gereği.</para>
///
/// <para><b>Statik, servis DEĞİL:</b> client projesindeki DI kayıtları server modülünde ELLE yapılmak
/// zorunda (client modülü server'ın DependsOn zincirinde değil) ve unutulunca bileşen <c>[Inject]</c>
/// anında circuit'i düşürüyor — 2026-08-10'da tam bu yaşandı. Durumu olmayan bir eşleme için o riski
/// almanın anlamı yok.</para>
/// </summary>
public static class ProductCommoditySeed
{
    /// <summary>Ailenin düzenleme formu; eşlemesi olmayan aile için <c>null</c> (çağıran sessizce geçmez).</summary>
    public static Type? EditComponentOf(ProcessType family)
    {
        return family switch
        {
            ProcessType.Metal   => typeof(Metals.MetalEditHost),
            ProcessType.Scrap   => typeof(Scraps.ScrapEditHost),
            ProcessType.Future  => typeof(Futures.FutureEditHost),
            ProcessType.Jewelry => typeof(Jewelries.JewelryEditHost),
            ProcessType.Stone   => typeof(Stones.StoneEditHost),
            ProcessType.Good    => typeof(Goods.GoodEditHost),
            ProcessType.Service => typeof(Services.ServiceEditHost),
            _                   => null,
        };
    }

    /// <summary>
    /// Forma geçilecek tohum parametreleri.
    ///
    /// <para>Mamülde ZENGİN tohum (<c>SeedModel</c>): kod/ad/KDV'nin yanında nitelik + varyant grafı ve
    /// medya bağları da taşınır. Diğer ailelerde yalnız kod/ad (<c>SeedCode</c>/<c>SeedName</c>).</para>
    ///
    /// <para>Popup kipinde "Kaydet ve Yeni" ile "Sil" KAPATILIR: buraya tek bir emtia yaratmak için
    /// gelinir; seri giriş ve silme bu akışın işi değil.</para>
    /// </summary>
    public static async Task<Dictionary<string, object>> BuildExtraParamsAsync(
        ProcessType family,
        Guid productId,
        string? productCode,
        string? productName,
        IProductAppService products)
    {
        var extra = BuildPopupParams();

        // Zengin mamül tohumu yalnız KAYITLI üründe (projeksiyon sunucudan okunur); kayıtsız üründe (Id boş —
        // combo "+" kayıtsız formda) kod/ad tohumuna düşülür, hata değil.
        if (family == ProcessType.Good && productId != Guid.Empty)
        {
            extra["SeedModel"] = await products.ProjectToGoodAsync(productId);
            return extra;
        }

        if (!string.IsNullOrWhiteSpace(productCode))
        {
            extra["SeedCode"] = productCode;
        }

        if (!string.IsNullOrWhiteSpace(productName))
        {
            extra["SeedName"] = productName;
        }

        return extra;
    }

    /// <summary>Tohumsuz popup parametreleri — tohumlanacak aday yokken (sınıflandırma panelinde seçim
    /// yapılmamışken) da footer aynı biçimde daraltılır; bayrak çifti tek yerde yaşar.</summary>
    public static Dictionary<string, object> BuildPopupParams()
    {
        return new Dictionary<string, object>
        {
            ["SupportsSaveAndNew"] = false,
            ["SupportsDelete"] = false,
        };
    }
}
