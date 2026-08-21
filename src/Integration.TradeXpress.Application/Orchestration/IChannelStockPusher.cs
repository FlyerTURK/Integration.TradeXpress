using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// Ürünün kanal listelemelerine STOK+FİYAT senkronu gönderme soyutlaması (ADR: mock-first — testler sahte
/// implementasyonla koşar, pazaryerine TEK istek gitmez). Orkestrasyon job'u YALNIZ bunu tanır.
/// </summary>
public interface IChannelStockPusher
{
    /// <summary>Ürünün tüm kanal-ürünlerini hafif senkronla tazeler. Push HATASI fırlatılmaz — loglanır:
    /// bir kanalın geçici arızası diğer kanalları ve job'ın kendisini düşürmemeli (retry sonraki tetikte).</summary>
    Task PushProductAsync(Guid productId);
}

/// <summary>
/// TEK BİR KANALIN push üyesi. Job bunu görmez — <see cref="IChannelStockPusher"/> (composite) görür.
///
/// <para><b>Neden ayrı arayüz:</b> job tek bir <c>IChannelStockPusher</c> enjekte ediyor. İki somut sınıf aynı
/// arayüzü uygulasaydı hangisinin çözüleceği KAYIT SIRASINA kalırdı — yani bir kanal sessizce hiç push
/// edilmezdi. Üyeler ayrı arayüzde durunca composite hepsini <c>IEnumerable</c> ile toplar ve yeni kanal
/// eklemek yalnız yeni bir üye sınıfı yazmak olur.</para>
///
/// <para><b>⚠ Uygulayan sınıflar <c>[ExposeServices(typeof(IChannelStockPusherMember))]</c> TAŞIMALIDIR:</b>
/// sınıf adları bu arayüzün adıyla bitmediği için ABP'nin varsayılan kaydı arayüzü AÇMAZ ve composite boş
/// koleksiyon alır — hiçbir kanal push edilmez, hata da çıkmaz. (Aynı tuzak 2026-08-08'de
/// <c>ICommodityStockReader</c>'da 14 gün yaşandı.) Konvansiyon testi: <c>DependencyRegistrationConventionTests</c>.</para>
/// </summary>
public interface IChannelStockPusherMember
{
    /// <summary>Log/teşhis için kanal adı.</summary>
    string ChannelName { get; }

    Task PushProductAsync(Guid productId);
}

/// <summary>
/// GERÇEK pusher — N11 hafif stok+fiyat yolu (<c>SyncStockAndPriceAsync</c>: dirty-tracking'li, değişmemişse
/// N11'e HİÇ yazmaz → fiili debounce). Trendyol'un hafif yolu YOK (yalnız tam submit) — Dilim 2'de
/// updatePriceAndInventory client'ı gelince buraya eklenir (ADR "Dilimler").
/// <para><b>UoW sözleşmesi (2026-07-25 inceleme bulgusu #9):</b> DB okuma/yazma KENDİ kısa UoW'unda biter;
/// N11 HTTP çağrıları UoW DIŞINDA koşar — 60sn'lik dış istek açık DB transaction'ı rehin almaz.</para>
/// </summary>
[ExposeServices(typeof(IChannelStockPusherMember))]
public class N11ChannelStockPusher : IChannelStockPusherMember, ITransientDependency
{
    private readonly IRepository<SalesChannelTrN11Product, Guid> _n11ProductRepository;
    private readonly ISalesChannelTrN11ProductAppService _n11ProductAppService;
    private readonly ChannelOverrideAuthority _overrideAuthority;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly ILogger<N11ChannelStockPusher> _logger;

    public N11ChannelStockPusher(
        IRepository<SalesChannelTrN11Product, Guid> n11ProductRepository,
        ISalesChannelTrN11ProductAppService n11ProductAppService,
        ChannelOverrideAuthority overrideAuthority,
        IUnitOfWorkManager unitOfWorkManager,
        IAsyncQueryableExecuter asyncExecuter,
        ILogger<N11ChannelStockPusher> logger)
    {
        _n11ProductRepository = n11ProductRepository;
        _n11ProductAppService = n11ProductAppService;
        _overrideAuthority = overrideAuthority;
        _unitOfWorkManager = unitOfWorkManager;
        _asyncExecuter = asyncExecuter;
        _logger = logger;
    }

    public string ChannelName => "N11";

    public virtual async Task PushProductAsync(Guid productId)
    {
        List<Guid> channelProductIds;

        // ── DB adımı: kendi kısa UoW'u (bulgu #9 — job'ın çağrı yerinde ambient UoW yok; repository çağrısı
        //    UoW'suz patlar ya da push süresince açık kalan transaction'a yapışırdı).
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            // IsActive TEK sorguda okunur (ayrı bir "aktif mi" sorgusu açmadan): aşağıda iki farklı karar
            // besliyor — gölge temizliği TÜM kayıtlarla, senkron listesi YALNIZ aktif olanlarla.
            var rows = await _asyncExecuter.ToListAsync(
                (await _n11ProductRepository.GetQueryableAsync())
                    .Where(p => p.ProductId == productId)
                    .Select(p => new { p.Id, p.IsActive }));

            if (rows.Count > 0)
            {
                // Gölge temizliği KANAL-AGNOSTİK servistedir (ChannelOverrideAuthority) — aynı delik
                // Trendyol/Etsy'de de vardı; N11'e gömülü kalsaydı orada açık kalırdı.
                //
                // Bu yüzden tetiği PASİF kayıt da verir: koşulu yukarıdaki IsActive süzgecine bağlasaydık,
                // N11 satırları pasif ama Trendyol satırları aktif olan bir üründe temizlik hiç koşmaz ve
                // Trendyol push'u bayat OverrideStock gölgesini taşırdı (TrendyolChannelStockPusher
                // temizliği bilinçli olarak çağırmıyor — "N11 üyesinde zaten çağrılıyor").
                await _overrideAuthority.ClearShadowedStockAsync(productId);
            }

            // PASİF kanal ürünü senkron kapsamı DIŞINDADIR — TrendyolChannelStockPusher'daki
            // ".Where(... && p.IsActive)" süzgecinin N11 karşılığı (2026-08-21'de portlandı; o güne kadar
            // YALNIZ Trendyol'da vardı).
            //
            // ASİMETRİ NEDEN TEHLİKELİYDİ: SalesChannelTrN11ProductRemover.DeactivateForProductAsync ürün
            // pasifleşince kanal kaydının IsActive'ini düşürüyor ve kendi doc'unda "pasif kayıt push/senkron
            // kapsamı dışında kalır (stok tetiği IsActive süzer)" diyordu — ama süzgeç YOKTU. Sonuç: 15
            // dakikalık repricing turu pasif kayda fiyat/stok yazmaya DEVAM ediyor, N11 listelemesindeki adet
            // her turda tazeleniyordu. Ne hata, ne uyarı, ne log çıkıyordu: kullanıcı ürünü "kaldırdım"
            // sanıyor, durumu ancak o listelemeden SİPARİŞ gelince fark ediyordu.
            channelProductIds = rows.Where(r => r.IsActive).Select(r => r.Id).ToList();

            await uow.CompleteAsync();
        }

        // ── HTTP adımı: UoW DIŞI — N11 senkronu kendi app-service UoW'unu açar; hata fırlatılmaz.
        foreach (var channelProductId in channelProductIds)
        {
            try
            {
                await _n11ProductAppService.SyncStockAndPriceAsync(channelProductId);
            }
            catch (Exception ex)
            {
                // Kanal arızası job'ı DÜŞÜRMEZ: stok DB'de zaten güncel; push sonraki tetikte/elle tekrarlanır.
                // Sessiz yutma değil — ürün+kanal kimliğiyle loglanır.
                _logger.LogWarning(ex,
                    "Kanal stok push başarısız: Product={ProductId}, N11ChannelProduct={ChannelProductId}. "
                    + "Stok DB'de güncel; push sonraki tetikte tekrarlanır.", productId, channelProductId);
            }
        }
    }

}
