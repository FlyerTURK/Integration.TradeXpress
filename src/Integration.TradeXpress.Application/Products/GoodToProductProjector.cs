using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Vouchers;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Products;

/// <summary>
/// MAMÜL → ÜRÜN PROJEKSİYONU — <see cref="ProductToGoodProjector"/>'ün TERSİ (2026-08-10 Hakan isteği:
/// "mamülden ürün üretimi de olabilir").
///
/// <para><b>ORTAK SINIF ARTIK PAYLAŞILIYOR</b> (2026-08-20): kod/ad/açıklama + medya + nitelik/varyant grafını
/// taşıyan iş <see cref="CommodityToProductProjector"/>'a taşındı; yedi emtia ailesinin tamamı aynı sınıfı
/// kullanıyor. Burada kalan tek şey MAMÜLE ÖZGÜ olan iki şey: kaydı okumak ve KDV oranını ürün ölçeğine
/// çevirmek. <b>Davranış değişmedi</b> — mamülün eski projeksiyonu neyi taşıyorsa aynısını taşır.</para>
///
/// <para><b>Neden sınıf ayakta kalıyor:</b> mamülün KDV'si vardır ve diğer altı ailede yoktur; ayrıca
/// "mamülün ürün projeksiyonu" adı çağrı yerlerinde (app service + konvansiyon testi) anlam taşır.
/// <see cref="CommodityToProductProjector"/>'ı çağıran ince bir kabuk, o farkı kaybetmeden tekrarı kaldırır.</para>
///
/// <para><b>FİYAT TAŞINMAZ</b> — mamülde fiyat VARYANTTA yaşar (<c>GoodVariantDetail.EntryPrice</c>);
/// üründe satış fiyatı reçeteden türetilen maliyetin üzerine kurulur. Gerekçenin tamamı
/// <see cref="CommodityToProductProjector"/>'ın özetinde.</para>
///
/// <para><b>Kaydetmez.</b> Yalnız forma seed üretir; kullanıcı ürüne ÖZEL alanları (kategori, reçete,
/// kargo desisi) doldurup kendisi kaydeder.</para>
/// </summary>
public class GoodToProductProjector : ITransientDependency
{
    private readonly IRepository<Good, Guid> _goodRepository;
    private readonly CommodityToProductProjector _projector;

    public GoodToProductProjector(
        IRepository<Good, Guid> goodRepository,
        CommodityToProductProjector projector)
    {
        _goodRepository = goodRepository;
        _projector = projector;
    }

    /// <summary>Mamülün ürün projeksiyonunu üretir (PERSİSTSİZ).</summary>
    public virtual async Task<ProductGetDto> ProjectAsync(Guid goodId)
    {
        var good = await _goodRepository.FindAsync(goodId)
            ?? throw new BusinessException("TradeXpress:Good:NotFound");

        return await _projector.ProjectAsync(new CommodityProjectionSource(
            good.Id,
            good.Code,
            good.Name,
            good.Description,
            CommodityProjectionShapes.Of(ProcessType.Good),
            VatRate: ToProductVatRate(good.VatSaleRate)));
    }

    /// <summary>
    /// KDV mamülün SATIŞ oranından gelir (alış değil): ürün satış tarafının kaydıdır ve kanal kayıtları bu
    /// oranı devralır.
    ///
    /// <para>Mamülde oran ondalıklı, üründe tam sayı tutulur — mamül %10,5 gibi bir oran taşıyorsa
    /// yuvarlanır; sessizce kırpmak yerine en yakın tam sayıya gider.</para>
    /// </summary>
    private static int ToProductVatRate(decimal goodVatSaleRate)
    {
        return (int)Math.Round(goodVatSaleRate, MidpointRounding.AwayFromZero);
    }
}
