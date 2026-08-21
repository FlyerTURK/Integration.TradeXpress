using System;
using System.Collections.Generic;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Variants;

namespace Integration.TradeXpress.Products;

/// <summary>
/// KAYDEDİLMEMİŞ ÜRÜNÜN SEED'İ (2026-08-20) — köprünün ileri yönünü ürün kaydından KURTARAN girdi.
///
/// <para><b>Çözdüğü sorun:</b> ürün → emtia seed'inin tamamı aslında GRAFTIR; kaydı şart koşan tek nokta
/// <see cref="ProductCommodityProjectionBuilder"/>'ın <c>FindAsync(productId)</c> satırıydı ve o satır Product
/// kaydından yalnız BEŞ skaler okur (kod · ad · açıklama · şirket · KDV). Nitelik, varyant ve iki bağlam medya
/// zaten uydu tablolardan (<c>EntityName + EntityId</c>) geliyordu; aynı veri kullanıcının AÇIK FORMUNDA da
/// duruyor. Bu yüzden kayıtsız üründe zengin seed sessizce düşürülüyor, kullanıcı emtia formunu elle
/// dolduruyordu (istemci bunu <c>ProductCommoditySeed</c>'de açıkça beyan ediyordu: "zengin seed yalnız
/// KAYITLI üründe").</para>
///
/// <para><b>Neden AYRI bir endpoint, "ProductId ya da taslak" taşıyan tek DTO değil:</b> "ikisinden TAM BİRİ dolu"
/// kuralını derleyici denetleyemez; çalışma anında ikisi de boş ya da ikisi de dolu gelen bir çağrı ancak
/// belgeyle yasaklanabilirdi. Kayıtlı ürün yolu (<c>Guid</c> alan endpoint'ler) olduğu gibi DURUYOR — bu tip yalnız
/// kaydı OLMAYAN ürünün yolunu açar.</para>
///
/// <para><b>ŞİRKET BURADAN GELMEZ:</b> sahiplik istemcinin beyanı değil, çalışılan şirkettir
/// (<c>CompanyOwnershipGuard.ResolveOwnerCompanyId</c>) — emtia kaydının kendisi de zaten böyle damgalanıyor.
/// Alanı DTO'ya koymak, istemcinin başka bir şirketin kaydını seed'leyebileceği izlenimini verirdi.</para>
///
/// <para><b>REÇETE TAŞINMAZ:</b> reçete satırı varyantın DB kimliğine yazılır ve o kimlik ancak ürün
/// kaydedilince doğar. <c>ProjectDraftTo*Async</c> kayıtsız çalışır, reçete yayılımı çalışmaz — bu bir eksiklik değil,
/// köprünün gerçek yapısal sınırıdır.</para>
/// </summary>
public class ProductDraftSeedDto
{
    /// <summary>Ürünün formdaki kodu. Emtianın kodu bundan doğar (kod benzersizliği emtia kaydında sınanır).</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>KDV oranı — yalnız KARŞILIĞI OLAN ailede (bugün Mamül) tüketilir. <c>null</c> = beyan edilmedi;
    /// tüketen taraf kendi varsayılanına düşer (uydurma oran üretilmez).</summary>
    public int? VatRate { get; set; }

    /// <summary>KAYIT-GENELİ medya bağları. Görsel panele eklendiği anda kütüphaneye yüklendiği için
    /// <c>MediaId</c> ürün kaydedilmeden ÖNCE de gerçektir — bağ kopyalanabilir, dosya yeniden yüklenmez.</summary>
    public List<EntityMediaLinkEditDto> Media { get; set; } = new();

    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();

    /// <summary>Varyant grafı — her varyant KENDİ medyasıyla. <c>CombinationKey</c> ve değer
    /// <c>ClientKey</c>'leri kaydedilmemiş grafta da DOLUDUR (istemci üretir, DB'den gelmez); köprünün
    /// varyant özelleştirmelerini hedef kayda oturtması bu imzaya bağlıdır.</summary>
    public List<EntityVariantGraphDto> Variants { get; set; } = new();
}
