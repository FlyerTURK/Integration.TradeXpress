using System;
using System.Linq;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// KÖPRÜNÜN AİLE SINIFLANDIRMASI TEK KAYNAKTADIR (2026-08-20 birleştirmesi).
///
/// <para><b>Bu testin sabitlediği risk:</b> aynı üç-kategori sınıflandırması (① tam varyantlı · ② uzantısız
/// varyantlı · ③ varyantsız) köprünün iki yönünde AYRI AYRI beyan ediliyordu. Sapma SESSİZDİR: yanlış şekil
/// istisna fırlatmaz, yalnız emtia formu eksik açılır (varyant sekmesi boş, görsel gelmez) ve kullanıcı bunu
/// ancak veriyi ikinci kez elle girerken fark eder. Artık ileri yön şekli tablodan TÜRETİLİR; bu test
/// türetmenin tabloyla aynı cevabı verdiğini ve YEDİ ailenin de tanındığını doğrular.</para>
/// </summary>
public class CommodityProjectionShapeTests
{
    [Fact]
    public void Every_family_the_bridge_knows_derives_its_forward_shape_from_the_same_table()
    {
        CommodityProjectionShapes.Families.Count.ShouldBe(7, "Köprü YEDİ aileyi tanır (CLAUDE.md §6).");

        foreach (var family in CommodityProjectionShapes.Families)
        {
            var table = CommodityProjectionShapes.Of(family);
            var forward = CommodityProjectionShapes.ForwardShapeOf(family);

            var expected = table.CarriesVariantGraph
                ? ProductProjectionShape.FullGraph
                : table.RecordMediaContext is null
                    ? ProductProjectionShape.Identity
                    : ProductProjectionShape.RecordMedia;

            forward.ShouldBe(expected, $"{family} için ileri yön şekli tabloyla çelişiyor.");
        }
    }

    [Theory]
    [InlineData(ProcessType.Metal, ProductProjectionShape.FullGraph)]
    [InlineData(ProcessType.Good, ProductProjectionShape.FullGraph)]
    [InlineData(ProcessType.Jewelry, ProductProjectionShape.FullGraph)]
    [InlineData(ProcessType.Stone, ProductProjectionShape.RecordMedia)]
    [InlineData(ProcessType.Scrap, ProductProjectionShape.Identity)]
    [InlineData(ProcessType.Future, ProductProjectionShape.Identity)]
    [InlineData(ProcessType.Service, ProductProjectionShape.Identity)]
    public void The_three_categories_are_pinned_family_by_family(ProcessType family, ProductProjectionShape expected)
    {
        // Bu tablo kuralın KENDİSİDİR: Taş varyantsız olmasına RAĞMEN kayıt-geneli medya taşır (iki ayrı
        // soru), Hurda/Vadeli/Hizmet ise hiç medya taşımaz — "alanı olmayana veri uydurulmaz".
        CommodityProjectionShapes.ForwardShapeOf(family).ShouldBe(expected);
    }

    [Fact]
    public void An_unknown_family_fails_fast_instead_of_defaulting_to_the_narrowest_shape()
    {
        // Varsayılan üretmek, sekizinci aileyi sessizce "varyantsız + medyasız" tarafa düşürürdü.
        var unknown = Enum.GetValues<ProcessType>()
            .FirstOrDefault(p => !CommodityProjectionShapes.Families.Contains(p));

        if (unknown == default && CommodityProjectionShapes.Families.Contains(default))
        {
            return;   // Tüm ProcessType değerleri köprüde tanımlıysa iddia konusuzdur.
        }

        Should.Throw<BusinessException>(() => CommodityProjectionShapes.ForwardShapeOf(unknown))
            .Code.ShouldBe("TradeXpress:Commodity:UnknownProjectionFamily");
    }
}
