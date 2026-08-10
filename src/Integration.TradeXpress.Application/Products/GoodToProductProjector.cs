using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Variants;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Products;

/// <summary>
/// MAMÜL → ÜRÜN PROJEKSİYONU — <see cref="ProductToGoodProjector"/>'ün TERSİ (2026-08-10 Hakan isteği:
/// "mamülden ürün üretimi de olabilir").
///
/// <para><b>Neden simetrik bir ikiz meşru:</b> <c>Product</c> ile <c>Good</c> zaten aynı jenerik altyapıyı
/// paylaşır (aynı <c>EntityVariant</c> sistemi, aynı DAM medya bağlam-çifti deseni, aynı nitelik grafı).
/// İki yön de aynı işi yapar: kullanıcının ELDEKİ kaydı ikinci kez yazmasını önlemek. Kataloğa mamül olarak
/// girilmiş bir kalemi satışa çıkarmak isteyen kullanıcı, bugün kod/ad/görsel/varyantları elle tekrar
/// giriyordu.</para>
///
/// <para><b>FİYAT TAŞINMAZ</b> — bu, ileri yöndeki projeksiyonla arasındaki EN ÖNEMLİ farktır ve kasıtlıdır.
/// Mamülde fiyat VARYANTTA yaşar (<c>GoodVariantDetail.EntryPrice</c>/<c>ExitPrice</c>); üründe ise satış
/// fiyatı reçeteden türetilen maliyetin üzerine kurulur. Mamülün giriş fiyatını ürünün satış fiyatına
/// yazmak, maliyeti fiyat sanmak olurdu ve orkestrasyon o değeri sessizce ezerdi. Fiyat alanı BOŞ doğar;
/// ürünün fiyat zinciri reçete kurulduktan sonra kendi cevabını üretir.</para>
///
/// <para><b>YALNIZ Good ↔ Product.</b> Maden/Mücevher/Taş ürüne paralel DEĞİLDİR (milyem/karat semantiği olan
/// hammaddedir); bu projeksiyon onlara genelleştirilmez — olmayan bir benzerliği varmış gibi göstermek
/// olurdu (ileri yöndeki projektörün özetindeki aynı gerekçe).</para>
///
/// <para><b>Kaydetmez.</b> Yalnız forma tohum üretir; kullanıcı ürüne ÖZEL alanları (kategori, reçete,
/// kargo desisi) doldurup kendisi kaydeder. Sessizce kayıt açmak, sınıflandırmanın MANUEL olması kuralını
/// delerdi.</para>
/// </summary>
public class GoodToProductProjector : ITransientDependency
{
    private const string GoodEntityName = "Good";

    private readonly IRepository<Good, Guid> _goodRepository;
    private readonly IEntityVariantGraphService _entityVariant;
    private readonly IEntityMediaAppService _entityMedia;

    public GoodToProductProjector(
        IRepository<Good, Guid> goodRepository,
        IEntityVariantGraphService entityVariant,
        IEntityMediaAppService entityMedia)
    {
        _goodRepository = goodRepository;
        _entityVariant = entityVariant;
        _entityMedia = entityMedia;
    }

    /// <summary>Mamülün ürün aynasını üretir (PERSİSTSİZ).</summary>
    public virtual async Task<ProductGetDto> ProjectAsync(Guid goodId)
    {
        var good = await _goodRepository.FindAsync(goodId)
            ?? throw new BusinessException("TradeXpress:Good:NotFound");

        var dto = new ProductGetDto
        {
            Code        = good.Code,
            Name        = good.Name,
            Description = good.Description,
            IsActive    = true,

            // KDV mamülün SATIŞ oranından gelir (alış değil): ürün satış tarafının kaydıdır ve kanal
            // kayıtları bu oranı devralır. Mamülde oran ondalıklı, üründe tam sayı tutulur — mamül %10,5
            // gibi bir oran taşıyorsa yuvarlanır; sessizce kırpmak yerine en yakın tam sayıya gider.
            VatRate = (int)Math.Round(good.VatSaleRate, MidpointRounding.AwayFromZero),
        };

        // MEDYA BAĞLAM-BAĞLAM KOPYALANIR (ileri yöndeki kararın aynısı): kayıt geneli → kayıt geneli,
        // varyant → varyant. İki depo AYRI kalır ve biri diğerinden TÜRETİLMEZ. Bağlar MediaId ile
        // linklenir, dosya YENİDEN YÜKLENMEZ — medya içeriği değişmezdir (aynı ContentHash ikinci kayıt açmaz).
        dto.Media = await _entityMedia.GetForAsync(MediaEntityNames.Good, good.Id);

        var graph = await _entityVariant.LoadGraphAsync(GoodEntityName, good.Id);

        dto.Attributes = graph.Attributes
            .Where(a => !a.IsDeleted)
            .Select(a => new EntityAttributeGraphDto
            {
                Name         = a.Name,
                DisplayOrder = a.DisplayOrder,
                Values = a.Values
                    .Where(v => !v.IsDeleted)
                    .Select(v => new EntityAttributeValueGraphDto
                    {
                        Value        = v.Value,
                        DisplayOrder = v.DisplayOrder,
                    })
                    .ToList(),
            })
            .ToList();

        // VARYANTLAR TAŞINIR, YENİDEN ÜRETİLMEZ: kartezyeni yeniden kurmak, kullanıcının mamül üzerinde
        // yaptığı elemeleri (silinmiş kombinasyonlar) geri getirirdi.
        //
        // SalePrice/RecipeLines BİLEREK BOŞ — gerekçe sınıf özetinde (fiyat taşınmaz; reçete ürünün kendi
        // sınıflandırma adımında kurulur).
        dto.Variants = new List<ProductVariantGraphDto>();
        foreach (var v in graph.Variants.Where(x => !x.IsDeleted))
        {
            var projected = new ProductVariantGraphDto
            {
                IsMain      = v.IsMain,
                Code        = v.Code,
                Name        = v.Name,
                Description = v.Description,
                IsActive    = v.IsActive,
                Barcode     = v.Barcode,
                Gtin        = v.Gtin,
                Mpn         = v.Mpn,
            };

            // Kaynak varyantın DB kimliği yoksa (kaydedilmemiş graf düğümü) bağlanacak medya da yoktur.
            if (v.Id != Guid.Empty)
            {
                projected.Media = await _entityMedia.GetForAsync(MediaEntityNames.GoodVariant, v.Id);
            }

            dto.Variants.Add(projected);
        }

        // Hiç varyant yoksa form boş kalmasın — ana varyant KAYIT KODUYLA doğar ("ANAVARYANT" sentinel'i
        // yerine): tek varyant bir ayrım değildir, ayırt edici bir kod taşımasının anlamı yok. Sentinel'e
        // düşmek o kodun pazaryerine SKU olarak gitmesi demekti (2026-08-06 kararı).
        if (dto.Variants.Count == 0)
        {
            dto.Variants.Add(new ProductVariantGraphDto
            {
                IsMain   = true,
                Code     = good.Code,
                Name     = good.Name,
                IsActive = true,
            });
        }

        RewriteSentinelMainVariant(dto.Variants, good.Code, good.Name);

        return dto;
    }

    /// <summary>
    /// SENTINEL ONARIMI — ana varyantın kodu <c>ANAVARYANT</c> ise SAHİBİN koduna çevrilir.
    ///
    /// <para>Gerekçe ileri yöndeki ikizinde (<see cref="ProductToGoodProjector"/>) ayrıntılı: varyantsız
    /// kayıtta <c>LoadGraphAsync</c> boş liste DÖNDÜRMEZ, ana varyantı sentinel yer tutucusuyla üretir —
    /// bu yüzden "hiç varyant yoksa" dalı tek başına YETMEZ ve sentinel projeksiyonla taşınırdı.</para>
    ///
    /// <para>Yan kazanç: eski sentinel'li kayıtlar projeksiyondan geçtiklerinde kendini onarır.</para>
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
