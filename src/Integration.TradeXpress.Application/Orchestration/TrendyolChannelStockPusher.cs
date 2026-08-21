using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// TRENDYOL push üyesi — <see cref="N11ChannelStockPusher"/>'ın eşi.
///
/// <para><b>Neden bu sınıf MVP'nin eksik parçası:</b> <c>SyncStockAndPriceAsync</c> P4'te yazıldı ama üretimde
/// HİÇBİR ÇAĞIRANI yoktu — yani "çapraz-kanal aşırı satış deliği kapandı" demek erkendi. Stok değişimi
/// yalnız N11'e yansıyor, Trendyol bayat kalmaya devam ediyordu. Bu sınıf o boşluğu kapatır.</para>
///
/// <para><b>UoW sözleşmesi</b> N11 üyesiyle birebir: DB okuma kendi kısa UoW'unda biter, HTTP çağrıları UoW
/// DIŞINDA koşar — dış istek açık bir DB transaction'ını rehin almaz.</para>
///
/// <para><b>Gölge temizliği YOK:</b> <c>ChannelOverrideAuthority.ClearShadowedStockAsync</c> ürün başına
/// kanal-agnostiktir ve N11 üyesinde zaten çağrılıyor. İkinci kez çağırmak aynı işi tekrarlardı.</para>
/// </summary>
[ExposeServices(typeof(IChannelStockPusherMember))]
public class TrendyolChannelStockPusher : IChannelStockPusherMember, ITransientDependency
{
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _productRepository;
    private readonly ISalesChannelTrTrendyolProductAppService _appService;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly ILogger<TrendyolChannelStockPusher> _logger;

    public TrendyolChannelStockPusher(
        IRepository<SalesChannelTrTrendyolProduct, Guid> productRepository,
        ISalesChannelTrTrendyolProductAppService appService,
        IUnitOfWorkManager unitOfWorkManager,
        IAsyncQueryableExecuter asyncExecuter,
        ILogger<TrendyolChannelStockPusher> logger)
    {
        _productRepository = productRepository;
        _appService = appService;
        _unitOfWorkManager = unitOfWorkManager;
        _asyncExecuter = asyncExecuter;
        _logger = logger;
    }

    public string ChannelName => "Trendyol";

    public virtual async Task PushProductAsync(Guid productId)
    {
        List<Guid> channelProductIds;

        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            channelProductIds = await _asyncExecuter.ToListAsync(
                (await _productRepository.GetQueryableAsync())
                    .Where(p => p.ProductId == productId && p.IsActive)
                    .Select(p => p.Id));

            await uow.CompleteAsync();
        }

        foreach (var channelProductId in channelProductIds)
        {
            try
            {
                await _appService.SyncStockAndPriceAsync(channelProductId);
            }
            catch (Exception ex)
            {
                // Kanal arızası job'ı DÜŞÜRMEZ: stok DB'de zaten güncel; push sonraki tetikte tekrarlanır.
                // Beklenen dallar da buraya düşer ve bu NORMALDİR: devam eden batch (BatchInProgress),
                // doğrulama bekleyen ürün, hiç push edilmemiş kayıt (NotPushedYet). Uyarı seviyesinde
                // loglanması bilinçli — "neden bu ürün senkronlanmadı" sorusunun tek cevap yeri burası.
                _logger.LogWarning(ex,
                    "Kanal stok push başarısız: Product={ProductId}, TrendyolChannelProduct={ChannelProductId}. "
                    + "Stok DB'de güncel; push sonraki tetikte tekrarlanır.", productId, channelProductId);
            }
        }
    }
}

/// <summary>
/// TÜM kanal üyelerini sırayla çalıştıran composite — orkestrasyon job'ının gördüğü TEK pusher.
///
/// <para><b>Neden composite:</b> job tek bir <see cref="IChannelStockPusher"/> enjekte ediyor. İki somut sınıf
/// aynı arayüzü uygulasaydı hangisinin çözüleceği kayıt sırasına kalırdı — bir kanal sessizce hiç push
/// edilmezdi. Yeni kanal eklemek artık yalnız yeni bir <see cref="IChannelStockPusherMember"/> yazmaktır;
/// bu dosyaya dokunulmaz.</para>
///
/// <para><b>Üye izolasyonu:</b> bir kanalın arızası diğerlerini durdurmaz. Üye kendi içinde zaten yutuyor;
/// buradaki ikinci kalkan üyenin KENDİSİNİN patlamasına karşıdır (ör. DI/UoW hatası).</para>
/// </summary>
[ExposeServices(typeof(IChannelStockPusher))]
public class CompositeChannelStockPusher : IChannelStockPusher, ITransientDependency
{
    private readonly IEnumerable<IChannelStockPusherMember> _members;
    private readonly ILogger<CompositeChannelStockPusher> _logger;

    public CompositeChannelStockPusher(
        IEnumerable<IChannelStockPusherMember> members,
        ILogger<CompositeChannelStockPusher> logger)
    {
        _members = members;
        _logger = logger;
    }

    public virtual async Task PushProductAsync(Guid productId)
    {
        foreach (var member in _members)
        {
            try
            {
                await member.PushProductAsync(productId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Kanal push ayağı düştü: Channel={ChannelName}, Product={ProductId}. Diğer kanallar devam ediyor.",
                    member.ChannelName, productId);
            }
        }
    }
}
