using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>
/// ÜRÜNDEN EMTİA SEED'İ — aile → düzenleme formu eşlemesi ve seed parametreleri, TEK yerde.
///
/// <para><b>Neden ortak:</b> aynı eşlemeyi iki yer soruyor — sihirbazın sınıflandırma paneli ve reçete
/// panelindeki "Üründen" anahtarı. İkinci kopya, sekizinci bir aile eklendiğinde birinin sessizce eski
/// kalması demekti (bu projede tam bu desen defalarca yaşandı).</para>
///
/// <para><b>YEDİ AİLE DE ZENGİN SEED ALIR</b> (2026-08-20; CLAUDE.md §6 "ANA KÖPRÜ = Product"). Eski hâlde
/// yalnız Mamül zengin seed alıyor, diğer altısına kod/ad geçiliyordu — gerekçe "yalnız Good ürüne paralel"
/// idi ve BAYATTI: köprü kuralı çevrimin yedi ailede de olmasını istiyor. Taşınan alan kümesi ailenin
/// kategorisine göre değişir (varyantlı/varyantsız/kimlik-yalnız), <b>ama teknik alan ve özel kod HİÇBİRİNDE
/// taşınmaz</b> — onları kullanıcı emtia formunda verir. Uydurmak yerine SORMAK, "sınıflandırma manueldir,
/// yazılım tahmin etmez" kuralının gereği.</para>
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
    /// Forma geçilecek seed parametreleri.
    ///
    /// <para>ZENGİN seed (<c>SeedModel</c>) YEDİ ailede de geçilir; içeriği ailenin kategorisine göre
    /// değişir (bkz. <see cref="ProjectAsync"/>). Kayıtlı üründe sunucudan okunur, KAYITSIZ üründe
    /// <paramref name="draft"/> ile taşınır. Kod/ad seed'i (<c>SeedCode</c>/<c>SeedName</c>) artık yalnız
    /// SON ÇARE: ne kayıt ne de taslak varsa (ör. eşlemesi olmayan aile).</para>
    ///
    /// <para>Popup kipinde "Kaydet ve Yeni" ile "Sil" KAPATILIR: buraya tek bir emtia yaratmak için
    /// gelinir; seri giriş ve silme bu akışın işi değil.</para>
    /// </summary>
    public static async Task<Dictionary<string, object>> BuildExtraParamsAsync(
        ProcessType family,
        Guid productId,
        string? productCode,
        string? productName,
        IProductAppService products,
        ProductDraftSeedDto? draft = null)
    {
        var extra = BuildPopupParams();

        // ZENGİN SEED ARTIK İKİ HÂLDE DE GEÇER (2026-08-20): kayıtlı üründe projeksiyon SUNUCUDAN okunur,
        // kayıtsız üründe kullanıcının AÇIK FORMUNDAKİ graf taslak endpoint'ine gönderilir. Eskiden kayıtsız üründe
        // yalnız kod/ad geçiyordu ve kullanıcı nitelik/varyant/görseli emtia formuna İKİNCİ KEZ giriyordu;
        // kaybın sebebi verinin olmaması değil, imzanın Guid istemesiydi.
        if (productId != Guid.Empty)
        {
            var seed = await ProjectAsync(family, productId, products);
            if (seed is not null)
            {
                extra["SeedModel"] = seed;
                return extra;
            }
        }
        else if (draft is not null)
        {
            var seed = await ProjectDraftAsync(family, draft, products);
            if (seed is not null)
            {
                extra["SeedModel"] = seed;
                return extra;
            }
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

    /// <summary>
    /// Aile → PROJEKSİYON ENDPOINT'İ eşlemesi — <see cref="EditComponentOf"/> ile AYNI dosyada ve aynı sırada
    /// durur ki biri diğerinden sessizce eski kalmasın (sekizinci aile eklendiğinde iki switch de kırmızı
    /// yanmalı, yalnız biri değil).
    ///
    /// <para>Dönüş <c>object?</c>: hedef Blazor'ın <c>Dictionary&lt;string, object&gt;</c> parametre sözlüğü
    /// — orada tip zaten kaybolur. TİPLİ sözleşme app service endpoint'indedir (<c>ProjectTo{Aile}Async</c>
    /// her biri kendi <c>{Aile}GetDto</c>'sunu döner); burada tek tipsiz endpoint açmak o sözleşmeyi yok
    /// ederdi.</para>
    ///
    /// <para>Eşlemesi olmayan aile için <c>null</c> → çağıran kod/ad seed'ine düşer, sessizce boş form
    /// açmaz.</para>
    /// </summary>
    private static async Task<object?> ProjectAsync(ProcessType family, Guid productId, IProductAppService products)
    {
        return family switch
        {
            ProcessType.Metal   => await products.ProjectToMetalAsync(productId),
            ProcessType.Scrap   => await products.ProjectToScrapAsync(productId),
            ProcessType.Future  => await products.ProjectToFutureAsync(productId),
            ProcessType.Jewelry => await products.ProjectToJewelryAsync(productId),
            ProcessType.Stone   => await products.ProjectToStoneAsync(productId),
            ProcessType.Good    => await products.ProjectToGoodAsync(productId),
            ProcessType.Service => await products.ProjectToServiceAsync(productId),
            _                   => null,
        };
    }

    /// <summary>
    /// KAYDEDİLMEMİŞ ürünün projeksiyonu — aynı yedi aile, DB'siz endpoint'ler (2026-08-20).
    ///
    /// <para>Üstteki switch'in ikizidir ve BİLİNÇLE onun hemen altında durur: biri sekizinci aileyi eklerken
    /// diğerini unutursa fark yan yana görülür. Ayrı dosyaya koymak, tam olarak bu projede defalarca yaşanan
    /// "iki tablo sessizce ayrıştı" desenini davet ederdi.</para>
    /// </summary>
    private static async Task<object?> ProjectDraftAsync(ProcessType family, ProductDraftSeedDto draft, IProductAppService products)
    {
        return family switch
        {
            ProcessType.Metal   => await products.ProjectDraftToMetalAsync(draft),
            ProcessType.Scrap   => await products.ProjectDraftToScrapAsync(draft),
            ProcessType.Future  => await products.ProjectDraftToFutureAsync(draft),
            ProcessType.Jewelry => await products.ProjectDraftToJewelryAsync(draft),
            ProcessType.Stone   => await products.ProjectDraftToStoneAsync(draft),
            ProcessType.Good    => await products.ProjectDraftToGoodAsync(draft),
            ProcessType.Service => await products.ProjectDraftToServiceAsync(draft),
            _                   => null,
        };
    }

    /// <summary>
    /// AÇIK ürün formunun canlı modelinden taslak seed kurar — kaydedilmemiş üründe köprünün girdisi budur.
    ///
    /// <para><b>Kod/ad BURADA formdan alınır</b> (kayıtlı yolda sunucudan okunuyor): kaydedilmemiş üründe
    /// sunucuda okunacak bir satır yoktur, kullanıcının o an yazdığı değer TEK gerçektir.</para>
    ///
    /// <para><b>Silinmiş satırlar taşınmaz</b> — kullanıcının ürün üzerinde yaptığı elemeler emtiaya
    /// sızmamalı; sunucu tarafı da aynı süzgeci uygular (savunma iki yerde, çünkü bu endpoint dışarıdan da
    /// çağrılabilir).</para>
    /// </summary>
    public static ProductDraftSeedDto BuildDraft(ProductGetDto model)
    {
        var draft = new ProductDraftSeedDto
        {
            Code        = model.Code,
            Name        = model.Name,
            Description = model.Description,
            VatRate     = model.VatRate,
        };

        draft.Media.AddRange(model.Media);
        draft.Attributes.AddRange(model.Attributes);
        draft.Variants.AddRange(model.Variants);
        return draft;
    }

    /// <summary>SEED'SİZ popup parametreleri — seed'lenecek aday yokken (sınıflandırma panelinde seçim
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
