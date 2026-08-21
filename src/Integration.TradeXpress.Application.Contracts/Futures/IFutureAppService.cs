using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Futures;

public interface IFutureAppService : ICrudAppService<
    FutureGetDto,
    FutureListDto,
    Guid,
    FutureListRequestDto,
    FutureCreateDto,
    FutureUpdateDto>
{
    /// <summary>Vadeli süreç paneli combo'su için host‖own kayıtlar (koda göre sıralı, pasifler dahil).</summary>
    Task<List<FutureListDto>> GetPickerListAsync();

    /// <summary>Vadelinin ÜRÜN projeksiyonu (PERSİSTSİZ) — emtia ⇄ ürün köprüsünün GERİ yönü; ortak uygulama
    /// <c>CommodityToProductProjector</c>'dadır (yedi aile aynı sınıfı kullanır).
    /// <para>Kaydetmez, yalnız forma seed üretir: kod/ad/açıklama — varyant taşıyan ailede ayrıca nitelik +
    /// varyant grafı ve medya — taşınır; kategori · reçete · fiyat gibi ürüne ÖZEL alanları kullanıcı doldurup
    /// kendisi kaydeder. <b>Emtianın teknik alanları (milyem/faktör, takip birimi, giriş fiyatı) ve özel kodları
    /// TAŞINMAZ</b> — ürün müşteriye bakar, emtia tekniğe bakar.</para></summary>
    Task<ProductGetDto> ProjectToProductAsync(Guid futureId);
}
