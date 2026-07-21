using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Channels;

/// <summary>
/// Kanal kurulum ORKESTRATÖRÜ (kanal-nötr dispatcher): kanalı bulur (company-owned güvenlik sınırı) → tipini
/// concrete TPT alt-tipinden çözer → uygun <see cref="IChannelProvisioner"/>'a delege eder. Sağlayıcı yoksa dostane
/// "desteklenmiyor" adımı döner (throw etmez — panel yine açılır). Adımların resilient yürütülmesi provisioner
/// tabanındadır (<see cref="ChannelProvisionerBase"/>); burada yalnız çözümleme + delegasyon.
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class ChannelProvisioningAppService : TradeXpressAppService, IChannelProvisioningAppService
{
    private readonly IRepository<SalesChannelBase, Guid> _channelRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly IEnumerable<IChannelProvisioner> _provisioners;

    public ChannelProvisioningAppService(
        IRepository<SalesChannelBase, Guid> channelRepository,
        ICurrentCompany currentCompany,
        IEnumerable<IChannelProvisioner> provisioners)
    {
        _channelRepository = channelRepository;
        _currentCompany = currentCompany;
        _provisioners = provisioners;
    }

    public virtual async Task<ProvisioningResultDto> ProvisionAsync(Guid salesChannelId)
    {
        var channel = await GetOwnedChannelAsync(salesChannelId);
        var channelType = ResolveChannelType(channel);

        var provisioner = _provisioners.FirstOrDefault(p => p.ChannelType == channelType);
        if (provisioner is null)
        {
            return BuildUnsupportedResult(salesChannelId);
        }

        return await provisioner.ProvisionAsync(salesChannelId, CancellationToken.None);
    }

    /// <summary>Kanalı BU ŞİRKET kapsamında bulur (company-owned güvenlik sınırı); yoksa dostane hata.</summary>
    private async Task<SalesChannelBase> GetOwnedChannelAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var channel = await AsyncExecuter.FirstOrDefaultAsync(
            (await _channelRepository.GetQueryableAsync()).Where(x => x.Id == salesChannelId && x.CompanyId == companyId));
        if (channel is null)
        {
            throw new BusinessException("TradeXpress:ChannelProvisioning:ChannelNotFound");
        }

        return channel;
    }

    /// <summary>Kanal tipini concrete TPT alt-tipinden çözer (<see cref="SalesChannelAppService"/> ile aynı switch).</summary>
    private static SalesChannelType ResolveChannelType(SalesChannelBase channel)
    {
        return channel switch
        {
            SalesChannelTrTrendyol => SalesChannelType.TrTrendyol,
            SalesChannelEtsy => SalesChannelType.Etsy,
            _ => SalesChannelType.TrN11,
        };
    }

    /// <summary>Bu kanal tipi için henüz sağlayıcı yok — boş rapor + tek açıklayıcı Skipped adımı (panel yine açılır).</summary>
    private ProvisioningResultDto BuildUnsupportedResult(Guid salesChannelId)
    {
        return new ProvisioningResultDto
        {
            ChannelId = salesChannelId,
            Steps =
            {
                new ProvisioningStepResultDto(
                    "unsupported",
                    L["ChannelProvisioning:Step:Unsupported"],
                    ProvisioningStatus.Skipped,
                    L["ChannelProvisioning:UnsupportedMessage"]),
            },
        };
    }

    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:ChannelProvisioning:CompanyRequired");
        }

        return companyId;
    }
}
