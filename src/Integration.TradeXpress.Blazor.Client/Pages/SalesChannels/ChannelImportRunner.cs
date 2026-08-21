using System;
using System.Threading.Tasks;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>
/// "Mağazadan içe aktar" işinin TEK dağıtım noktası — kanal türüne göre doğru servisi çağırır.
///
/// <para><b>Neden ayrı servis:</b> aynı dağıtımı İKİ çağıran soruyor — kanal edit formundaki düğme
/// (<c>ChannelImportButton</c>) ve kanal listesinin araç çubuğu. Biri bileşen, diğeri toolbar aksiyonu
/// olduğundan kod paylaşımının başka yolu yok; kopyalasaydık dördüncü bir pazaryeri eklendiğinde biri
/// güncellenir, diğeri sessizce eski kalırdı (connascence-of-algorithm).</para>
///
/// <para><b>Desteklenmeyen tür SESSİZ GEÇMEZ:</b> boş bir "başarılı" özeti döndürmek, hiç çalışmayan bir
/// içe aktarımı olmuş gibi gösterirdi.</para>
/// </summary>
public class ChannelImportRunner : ITransientDependency
{
    private readonly ISalesChannelTrN11ProductAppService _n11AppService;
    private readonly ISalesChannelTrTrendyolProductAppService _trendyolAppService;
    private readonly ISalesChannelEtsyProductAppService _etsyAppService;

    public ChannelImportRunner(
        ISalesChannelTrN11ProductAppService n11AppService,
        ISalesChannelTrTrendyolProductAppService trendyolAppService,
        ISalesChannelEtsyProductAppService etsyAppService)
    {
        _n11AppService = n11AppService;
        _trendyolAppService = trendyolAppService;
        _etsyAppService = etsyAppService;
    }

    /// <summary>Kanalı içe aktarır ve (oluşan, güncellenen) sayılarını döndürür.
    /// <c>Supported=false</c> → o tür için içe aktarım yolu yok; çağıran kullanıcıyı bilgilendirir.</summary>
    public virtual async Task<ChannelImportOutcome> RunAsync(Guid salesChannelId, SalesChannelType channelType)
    {
        switch (channelType)
        {
            case SalesChannelType.TrN11:
            {
                var result = await _n11AppService.ImportFromMarketplaceAsync(salesChannelId);
                return new ChannelImportOutcome(true, result.CreatedChannelProducts, result.UpdatedChannelProducts);
            }

            case SalesChannelType.TrTrendyol:
            {
                var result = await _trendyolAppService.ImportFromMarketplaceAsync(salesChannelId);
                return new ChannelImportOutcome(true, result.CreatedChannelProducts, result.UpdatedChannelProducts);
            }

            case SalesChannelType.Etsy:
            {
                var result = await _etsyAppService.ImportFromMarketplaceAsync(salesChannelId);
                return new ChannelImportOutcome(true, result.CreatedChannelProducts, result.UpdatedChannelProducts);
            }

            default:
            {
                return new ChannelImportOutcome(false, 0, 0);
            }
        }
    }
}

/// <summary>İçe aktarım sonucu — üç pazaryerinin ayrı DTO'larından NÖTR sayılara indirgenmiş hâli.</summary>
public sealed record ChannelImportOutcome(bool Supported, int Created, int Updated);
