using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Variants;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Bir emtia kaydının ürüne TAŞINAN kimliği — projeksiyonun TEK girdisi.
///
/// <para><b>Neden kaydın kendisi değil bu snapshot:</b> ortak sınıf (<see cref="CommodityToProductProjector"/>)
/// hiçbir emtia tipini tanımaz. Tanısaydı yedi aileyi de bilmek zorunda kalır ve "şu ailede şu alan da taşınsın"
/// baskısı doğrudan buraya inerdi; taşınmayacak alanların (milyem/faktör, takip birimi, giriş fiyatı, özel
/// kodlar) o sınıfa ULAŞMAMASI yapısal bir güvencedir, bir dikkat meselesi değil.</para>
/// </summary>
/// <param name="OwnerId">Kaynak emtia kaydının kimliği — medya ve varyant grafı bununla okunur.</param>
/// <param name="VatRate">KDV oranı; <c>null</c> = bu ailede KDV karşılığı YOK (yalnız mamül taşır).</param>
public sealed record CommodityProjectionSource(
    Guid OwnerId,
    string Code,
    string Name,
    string? Description,
    CommodityProjectionShape Shape,
    int? VatRate = null);

/// <summary>
/// EMTİA → ÜRÜN PROJEKSİYONUNUN ORTAK SINIFI — yedi ailenin tamamı buradan geçer
/// (2026-08-20 Hakan talimatı: *"Üründen emtia, emtiadan ürüne … DİĞER EMTİA TÜRLERİ için de dönüşümleri
/// olgunlaştıralım"*).
///
/// <para><b>Köprü kuralı:</b> ana köprü <c>Product</c>'tır. Emtia ile kanal ürünü arasında doğrudan yol
/// açılmaz; her çevrim buradan geçer. Bu sınıf o köprünün GERİ yönüdür (ileri yön <c>ProductToGoodProjector</c>
/// ve <c>ProductCommodityProvisioner</c>).</para>
///
/// <para><b>NE TAŞINIR:</b> kod · ad · açıklama · KDV (karşılığı olan ailede) · kayıt-geneli medya ·
/// varyant taşıyan ailede nitelik + varyant grafı + varyant medyası.</para>
///
/// <para><b>NE TAŞINMAZ ve NEDEN:</b> emtianın TEKNİK alanları (milyem/faktör, takip birimi, stok birimi,
/// giriş fiyatı, min/max miktar) ürüne GİRMEZ — *"ürün müşteriye bakar, emtia tekniğe bakar"*; ürün teknik
/// alanı tanımlamaz, yalnız reçete satırı üzerinden TÜKETİR. ÖZEL KOD alanları (Marka · Model · Cins · Tür ·
/// Renk · Ölçü · Kategori · Grup kodu) kullanıcının kendi gruplama düzenidir; ürünün müşteriye dönük
/// niteliğiyle karıştırılırsa o düzen ürüne tabi kılınmış olur. Tedarikçi/doküman/not da taşınmaz.</para>
///
/// <para><b>FİYAT TAŞINMAZ.</b> Üründe satış fiyatı reçeteden türetilen maliyetin üzerine kurulur; emtianın
/// giriş fiyatını ürünün satış fiyatına yazmak maliyeti fiyat sanmak olurdu ve orkestrasyon o değeri
/// sessizce ezerdi. Alan BOŞ doğar.</para>
///
/// <para><b>KATEGORİ UYDURULMAZ.</b> <c>ProductCategoryId</c> boş kalır: kategori ürünün kendi sınıflandırma
/// adımıdır (form kademeli kilidinde ilk adım) ve emtiadan türetilemez. Emtianın "Kategori" özel kodu ürün
/// kategorisi DEĞİLDİR.</para>
///
/// <para><b>Kaydetmez.</b> Yalnız forma seed üretir; kullanıcı ürüne özel alanları doldurup kendisi
/// kaydeder. Sessizce kayıt açmak "sınıflandırma manueldir, yazılım tahmin etmez" kuralını delerdi.</para>
/// </summary>
public class CommodityToProductProjector : ITransientDependency
{
    private readonly IEntityVariantGraphService _entityVariant;
    private readonly IEntityMediaAppService _entityMedia;

    public CommodityToProductProjector(
        IEntityVariantGraphService entityVariant,
        IEntityMediaAppService entityMedia)
    {
        _entityVariant = entityVariant;
        _entityMedia = entityMedia;
    }

    /// <summary>Emtianın ürün projeksiyonunu üretir (PERSİSTSİZ).</summary>
    public virtual async Task<ProductGetDto> ProjectAsync(CommodityProjectionSource source)
    {
        var dto = new ProductGetDto
        {
            Code        = source.Code,
            Name        = source.Name,
            Description = source.Description,
            IsActive    = true,
            VatRate     = source.VatRate,
        };

        // MEDYA BAĞLAM-BAĞLAM KOPYALANIR: kayıt geneli → kayıt geneli, varyant → varyant. İki depo AYRI
        // kalır ve biri diğerinden TÜRETİLMEZ. Bağlar MediaId ile linklenir, dosya YENİDEN YÜKLENMEZ —
        // medya içeriği değişmezdir (aynı ContentHash ikinci kayıt açmaz).
        if (source.Shape.RecordMediaContext is { } recordMediaContext)
        {
            dto.Media = await _entityMedia.GetForAsync(recordMediaContext, source.OwnerId);
        }

        if (source.Shape.CarriesVariantGraph)
        {
            await CarryVariantGraphAsync(dto, source);
        }

        // VARYANTSIZ AİLE ya da hiç varyantı olmayan kayıt: ürün tarafı varyantsız OLAMAZ (SKU'yu varyant
        // taşır), o yüzden TEK ana varyantla doğar. Kodu "ANAVARYANT" sentinel'i değil KAYDIN kodudur —
        // tek varyant bir ayrım değildir ve o sentinel pazaryerine SKU olarak giderdi (2026-08-06 kararı).
        if (dto.Variants.Count == 0)
        {
            dto.Variants.Add(new ProductVariantGraphDto
            {
                IsMain   = true,
                Code     = source.Code,
                Name     = source.Name,
                IsActive = true,
            });
        }

        RewriteSentinelMainVariant(dto.Variants, source.Code, source.Name);

        return dto;
    }

    /// <summary>
    /// Nitelik + varyant grafını ORTAK sistemden okur (<c>EntityVariant</c> sahip bağlamı) ve ürün grafına
    /// taşır — yalnız varyant TAŞIYAN aileler için çağrılır.
    ///
    /// <para><b>Varyantlar TAŞINIR, YENİDEN ÜRETİLMEZ:</b> kartezyeni yeniden kurmak, kullanıcının emtia
    /// üzerinde yaptığı elemeleri (silinmiş kombinasyonlar) geri getirirdi.</para>
    /// </summary>
    private async Task CarryVariantGraphAsync(ProductGetDto dto, CommodityProjectionSource source)
    {
        var graph = await _entityVariant.LoadGraphAsync(source.Shape.EntityName, source.OwnerId);

        // NİTELİK GRAFI KİMLİKSİZ ama İSTEMCİ ANAHTARI KORUNARAK taşınır: Id YAZILMAZ (hedef ürünün kendi
        // düğümleri doğar, kaynağın düğümleri sahiplenilmez), ClientKey ise AYNEN kopyalanır.
        //
        // ClientKey burada bir DB kimliği DEĞİL, kombinasyonun istemci-taraflı imzasıdır: varyantın
        // CombinationKey'i tam olarak bu anahtarların sıralı birleşimidir. Yeni anahtar üretmek (DTO'nun
        // Guid.NewGuid() varsayılanı) imzayı KAYNAKSIZ bırakır — kayıtta SaveAttributesAsync yeni değerleri
        // DTO'nun ClientKey'iyle indeksler ve imzadaki anahtarlar o sözlükte bulunamaz.
        dto.Attributes = graph.Attributes
            .Where(a => !a.IsDeleted)
            .Select(a => new EntityAttributeGraphDto
            {
                ClientKey    = a.ClientKey,
                Name         = a.Name,
                DisplayOrder = a.DisplayOrder,
                Values = a.Values
                    .Where(v => !v.IsDeleted)
                    .Select(v => new EntityAttributeValueGraphDto
                    {
                        ClientKey    = v.ClientKey,
                        Value        = v.Value,
                        DisplayOrder = v.DisplayOrder,
                    })
                    .ToList(),
            })
            .ToList();

        // SalePrice/RecipeLines BİLEREK BOŞ — gerekçe sınıf özetinde (fiyat türetilir, reçete ürünün kendi
        // sınıflandırma adımında kurulur).
        dto.Variants = new List<ProductVariantGraphDto>();
        foreach (var variant in graph.Variants.Where(x => !x.IsDeleted))
        {
            var projected = new ProductVariantGraphDto
            {
                IsMain      = variant.IsMain,
                Code        = variant.Code,
                Name        = variant.Name,
                Description = variant.Description,
                IsActive    = variant.IsActive,
                Barcode     = variant.Barcode,
                Gtin        = variant.Gtin,
                Mpn         = variant.Mpn,
                Oem         = variant.Oem,

                // KOMBİNASYON İMZASI TAŞINIR — taşınan verinin KAYITTA hayatta kalmasının tek koşulu budur.
                // Ürünün kayıt yolu (ApplyVariantCustomizationsAsync) Id'siz bir satırı hedef varyanta YALNIZ
                // bu imzayla bağlar; imzası boş olan satır ANA varyant değilse null'a düşer, atlanır ve o
                // satırın barkodu/GTIN/MPN'i ile varyant MEDYASI sessizce kaybolurdu — hata görünmez, çünkü
                // varyantın kendisi synchronizer tarafından nitelik kartezyeninden yine üretilir.
                // Aynı imza İSTEMCİDE de gerekir: otomatik regen sonrası VariantGraphMerge seed'lenen satırları
                // bununla eşler; imzasız satır eşleşmez ve taze kartezyen onun yerine geçerdi.
                CombinationKey = variant.CombinationKey,

                // Kombinasyon ÖZETİ salt-okunur görüntüdür (kayıt yoksayar) — seed'lenen form, kaydın
                // varyantlarını kaynaktaki gibi okunur göstersin diye taşınır.
                AttributeSummary = variant.AttributeSummary,
            };

            // Kaynak varyantın DB kimliği yoksa (kaydedilmemiş graf düğümü) bağlanacak medya da yoktur.
            if (variant.Id != Guid.Empty && source.Shape.VariantMediaContext is { } variantMediaContext)
            {
                projected.Media = await _entityMedia.GetForAsync(variantMediaContext, variant.Id);
            }

            dto.Variants.Add(projected);
        }
    }

    /// <summary>
    /// SENTINEL ONARIMI — ana varyantın kodu <c>ANAVARYANT</c> ise SAHİBİN koduna çevrilir.
    ///
    /// <para><b>Neden "boş liste" dalı YETMİYOR</b> (2026-08-10, testle yakalandı): varyantsız kayıtta
    /// <c>LoadGraphAsync</c> boş liste DÖNDÜRMEZ — ana varyantı sentinel yer tutucusuyla üretip döndürür.
    /// Dolayısıyla o dal hiç çalışmıyor ve projeksiyon sentinel'i olduğu gibi taşıyordu.</para>
    ///
    /// <para><b>Neden kaynağında (LoadGraphAsync) değil BURADA:</b> yer tutucu, sahibin kimliğinin
    /// bilinmediği genel bir yükleme yolunda meşrudur (form kimlik alanını zaten gizler). Sahibin kodu
    /// yalnız BURADA biliniyor; kaynağı değiştirmek onu tüketen tüm formları etkilerdi.</para>
    /// </summary>
    private static void RewriteSentinelMainVariant(List<ProductVariantGraphDto> variants, string ownerCode, string ownerName)
    {
        foreach (var variant in variants)
        {
            if (string.Equals(variant.Code, EntityVariantConsts.MainVariantCode, StringComparison.Ordinal))
            {
                variant.Code = ownerCode;
                variant.Name = ownerName;
            }
        }
    }
}
