using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Trendyol;

/// <summary>
/// <see cref="ITrendyolCredentialResolver"/> — Trendyol kanal kaydından kimliği çözer. N11'in
/// <c>ResolveCurrentCompanyN11CredentialsAsync</c> deseniyle simetrik; farkı: Trendyol kimliği SellerId+ApiKey+ApiSecret
/// üçlüsü. <see cref="ApplicationService"/> türetir (CurrentCompany/AsyncExecuter gibi altyapıyı hazır alır).
///
/// <para>⚠ <b>HTTP API'den ÇEKİLİDİR</b> (2026-08-07 G1 — konvansiyon ağının yakaladığı açık):
/// <c>ApplicationService</c> türetmek ABP'nin otomatik API controller'ına kaydolmak demek; bu sınıfın dönüşü
/// KANAL SIRLARIDIR (ApiKey/ApiSecret) ve uç anonim erişilebilirdi. Sınıf yalnız SUNUCU-İÇİ bir yardımcıdır —
/// istemciye hiçbir koşulda sır dökülmez.</para>
/// </summary>
[RemoteService(IsEnabled = false)]
public class TrendyolCredentialResolver : ApplicationService, ITrendyolCredentialResolver
{
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly ICurrentCompany _currentCompany;

    public TrendyolCredentialResolver(
        IRepository<SalesChannelTrTrendyol, Guid> channelRepository,
        ICurrentCompany currentCompany)
    {
        _channelRepository = channelRepository;
        _currentCompany = currentCompany;
    }

    public virtual async Task<TrendyolCredentials> ResolveForCurrentCompanyAsync(CancellationToken cancellationToken = default)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:SalesChannel:CompanyRequired");
        }

        var channel = await AsyncExecuter.FirstOrDefaultAsync(
            (await _channelRepository.GetQueryableAsync()).Where(x => x.CompanyId == companyId));
        if (channel is null)
        {
            throw new BusinessException("TradeXpress:Trendyol:NoChannelForCompany");
        }

        return ToCredentials(channel);
    }

    public virtual async Task<TrendyolCredentials> ResolveBySalesChannelIdAsync(Guid salesChannelId, CancellationToken cancellationToken = default)
    {
        var channel = await AsyncExecuter.FirstOrDefaultAsync(
            (await _channelRepository.GetQueryableAsync()).Where(x => x.Id == salesChannelId));
        if (channel is null)
        {
            throw new BusinessException("TradeXpress:Trendyol:ChannelNotFound");
        }

        return ToCredentials(channel);
    }

    private static TrendyolCredentials ToCredentials(SalesChannelTrTrendyol channel)
    {
        return new TrendyolCredentials(channel.SellerId, channel.ApiKey, channel.ApiSecret);
    }
}
