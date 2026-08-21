using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.ObjectMapping;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// EMTİA → ÜRÜN PROJEKSİYONUNUN UÇTAN UCA AĞI — projeksiyon DTO'suna değil, KULLANICININ KAYDETTİĞİ SONUCA bakar.
///
/// <para><b>Neden ayrı bir test sınıfı:</b> <c>CommodityProductProjectionTests</c> projektörün ÜRETTİĞİ DTO'yu
/// doğrular ve orada her şey doğru görünür — varyantlar, barkodlar, varyant medyası taşınmış olur. Ama o DTO
/// kullanıcının önüne bir FORM olarak gider ve asıl soru şudur: <b>kullanıcı Kaydet'e bastıktan sonra taşınan
/// veri hâlâ duruyor mu?</b></para>
///
/// <para><b>Yakalanan kusur (2026-08-20 denetimi):</b> projeksiyon satırları kombinasyon imzasını
/// (<c>CombinationKey</c>) ve nitelik değerlerinin istemci anahtarlarını (<c>ClientKey</c>) taşımıyordu.
/// Ürünün kayıt yolu (<c>EntityVariantGraphService.ApplyVariantCustomizationsAsync</c>) Id'siz bir satırı
/// hedef varyanta yalnız bu imzayla bağlar; imzasız satır ANA varyant değilse <c>null</c>'a düşüp ATLANIR ve
/// uzantı geri-çağrısı (varyant medyasını yazan yer) o satır için hiç çalışmaz. Sonuç sessizdi: varyantların
/// kendisi synchronizer tarafından nitelik kartezyeninden yine üretildiği için form dolu görünüyor, yalnız
/// ana olmayan varyantların barkodu/GTIN/MPN'i ve GÖRSELLERİ kaybolmuş oluyordu.</para>
///
/// <para><b>Hata sınıfı:</b> "doğru DTO üret, yanlış anahtarla kaydet" — derleme geçer, projeksiyon testi
/// yeşil kalır, hata yalnız KAYITTAN SONRA görülür. KIRMIZIYSA imza/anahtar taşıma zinciri kırılmıştır;
/// testi gevşetme, zinciri düzelt.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class CommodityProductProjectionRoundTripTests : TradeXpressEntityFrameworkCoreTestBase
{
    /// <summary>Ana OLMAYAN varyanta yazılan çapa — kayıttan sonra hangi satırın hayatta kaldığını
    /// varyant Id'sine bağlı kalmadan tanımlar (ürün tarafında varyantlar YENİ kimliklerle doğar).</summary>
    private const string CarriedBarcode = "8690000000017";

    private readonly IMetalAppService _metalService;
    private readonly IProductAppService _productService;
    private readonly IProductCategoryAppService _categoryService;
    private readonly IRepository<CurrencyUnit, Guid> _units;
    private readonly IRepository<Media, Guid> _media;
    private readonly ICurrentCompany _currentCompany;
    private readonly IObjectMapper _objectMapper;

    public CommodityProductProjectionRoundTripTests()
    {
        _metalService    = GetRequiredService<IMetalAppService>();
        _productService  = GetRequiredService<IProductAppService>();
        _categoryService = GetRequiredService<IProductCategoryAppService>();
        _units           = GetRequiredService<IRepository<CurrencyUnit, Guid>>();
        _media           = GetRequiredService<IRepository<Media, Guid>>();
        _currentCompany  = GetRequiredService<ICurrentCompany>();
        _objectMapper    = GetRequiredService<IObjectMapper>();
    }

    /// <summary>Maden ① TAM VARYANTLI ailedir: projektör nitelik + varyant grafını ve varyant medyasını taşıdığını
    /// söyler. Bu test o iddiayı KAYIT SONRASINDA sınar.</summary>
    [Fact]
    public async Task A_projected_metal_keeps_its_non_main_variant_data_after_the_product_is_saved()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var unitId = await GetAnyUnitIdAsync();

            // 1) İKİ VARYANTLI MADEN — varyantlar nitelik kartezyeninden doğar, yani nitelik BAĞLARI (ve
            //    dolayısıyla kombinasyon imzası) GERÇEKTİR. Bağsız kurulmuş bir fixture bu testi anlamsız
            //    kılardı: imzası zaten boş olan satır üzerinden imza taşıma sınanamaz.
            var metal = await _metalService.CreateAsync(new MetalCreateDto
            {
                Code = "KOPRURT",
                Name = "Kopru Round Trip Madeni",
                FollowingUnitId = unitId,
                Attributes = new List<EntityAttributeGraphDto>
                {
                    new EntityAttributeGraphDto
                    {
                        Name = "Ayar",
                        Values = new List<EntityAttributeValueGraphDto>
                        {
                            new EntityAttributeValueGraphDto { Value = "22K" },
                            new EntityAttributeValueGraphDto { Value = "14K" },
                        },
                    },
                },
            });

            metal.Variants.Count.ShouldBe(2, "Fixture ön-koşulu: kartezyen iki varyant üretmeliydi.");

            // 2) ANA OLMAYAN varyanta barkod + varyant medyası — projektörün taşıdığını İDDİA ETTİĞİ veri.
            //    Ana varyant bilinçle seçilmiyor: ana varyant kayıtta IsMain bayrağıyla da çözülebildiği için
            //    kusuru GİZLERDİ (kusur yalnız ana OLMAYAN satırlarda görünür).
            var mediaId = await SeedMediaAsync(companyId);
            var source = metal.Variants.Single(v => !v.IsMain);
            source.Barcode = CarriedBarcode;
            source.Media.Add(new EntityMediaLinkEditDto { MediaId = mediaId, IsDefault = true });

            await _metalService.UpdateAsync(metal.Id, new MetalUpdateDto
            {
                Code = metal.Code,
                Name = metal.Name,
                FollowingUnitId = unitId,
                Factor = metal.Factor,
                IsActive = metal.IsActive,
                Attributes = metal.Attributes,
                Variants = metal.Variants,
            });

            // 3) PROJEKTÖR — projeksiyon satırları kombinasyon imzasını TAŞIMALI. Bu satır tek başına da bir
            //    çividir: imza boş dönerse aşağıdaki kayıt zaten sessizce veri düşürür.
            var projected = await WithUnitOfWorkAsync(() => _metalService.ProjectToProductAsync(metal.Id));

            projected.Variants.Count.ShouldBe(2);
            projected.Variants.ShouldAllBe(v => v.CombinationKey != string.Empty);
            projected.Variants.Single(v => v.Barcode == CarriedBarcode).Media
                .ShouldHaveSingleItem().MediaId.ShouldBe(mediaId);

            // 4) UÇTAN UCA — tohumlanan form KAYDEDİLİR. Kaydetme yolu birebir formunki: GetDto →
            //    CreateDto (ProductGetToCreateMapper) → CreateAsync. Elle kurulan bir CreateDto, imzayı
            //    mapper'ın düşürme ihtimalini testin kapsamı dışında bırakırdı.
            var input = _objectMapper.Map<ProductGetDto, ProductCreateDto>(projected);
            input.ProductCategoryId = await CreateCategoryAsync();   // ürüne ÖZEL alan: projeksiyondan geçmez

            var created = await _productService.CreateAsync(input);

            // 5) KAYITTAN SONRA: taşınan veri hâlâ duruyor ve KARDEŞ varyanta sızmamış olmalı.
            var reloaded = await _productService.GetAsync(created.Id);

            reloaded.Variants.Count.ShouldBe(2, "Nitelik grafı taşındıysa ürün de iki varyantla doğmalı.");

            var carried = reloaded.Variants.Where(v => v.Barcode == CarriedBarcode).ToList();
            carried.Count.ShouldBe(
                1,
                "Ana OLMAYAN varyantın taşınan verisi KAYITTA düştü: satır kombinasyon imzasıyla hedef " +
                "varyanta bağlanamayıp atlandı.");

            carried[0].Media.ShouldHaveSingleItem().MediaId.ShouldBe(
                mediaId,
                "Varyant medyası kayıtta yazılmadı: uzantı geri-çağrısı atlanan satır için hiç çalışmaz.");

            reloaded.Variants.Count(v => v.Media.Count > 0).ShouldBe(
                1,
                "Görsel kardeş varyanta sızmamalı — iki varyant AYRI bağlamlarda yaşar.");
            reloaded.Media.ShouldBeEmpty("Varyant görseli kayıt-geneli bağlama sızmamalı.");
        }
    }

    // ── Kurulum yardımcıları ────────────────────────────────────────────────────────────────────────

    /// <summary>DAM kütüphane kaydı — blob İÇERİĞİ gerekmez: bu test link zincirine bakar, baytlara değil
    /// (<c>MetalVariantPreviewTests</c> deseni).</summary>
    private async Task<Guid> SeedMediaAsync(Guid companyId)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var media = new Media(
                companyId: companyId,
                mediaType: MediaType.Image,
                blobName: "kopru-round-trip-blob",
                fileName: "varyant.png",
                contentType: "image/png",
                size: 3,
                contentHash: "kopru-round-trip-hash");
            media.SetPoster("kopru-round-trip-poster.jpg");
            await _media.InsertAsync(media, autoSave: true);
            return media.Id;
        });
    }

    /// <summary>Ürün kategorisi ZORUNLUDUR (kanal kategorisi ve komisyon o bağdan çözülür); testin konusu
    /// kategori olmadığından burada yalnız gürültü olarak kurulur.</summary>
    private async Task<Guid> CreateCategoryAsync()
    {
        var category = await _categoryService.CreateAsync(new ProductCategoryCreateDto
        {
            Name = "Kopru Round Trip Kategorisi",
        });

        return category.Id;
    }

    private async Task<Guid> GetAnyUnitIdAsync()
    {
        return await WithUnitOfWorkAsync(async () => (await _units.GetListAsync()).First().Id);
    }
}
