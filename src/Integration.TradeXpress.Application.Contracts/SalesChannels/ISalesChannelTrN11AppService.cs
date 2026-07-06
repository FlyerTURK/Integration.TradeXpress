using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>N11 satış kanalı CRUD (tipe-özel) — generic <c>ICrudAppService</c>; company-owned. Liste tür-bağımsız
/// <see cref="ISalesChannelAppService"/>'te; burada N11'e özel get/create/update (AppKey/AppSecret).</summary>
public interface ISalesChannelTrN11AppService : ICrudAppService<
    SalesChannelTrN11GetDto,
    SalesChannelListDto,
    Guid,
    SalesChannelListRequestDto,
    SalesChannelTrN11CreateDto,
    SalesChannelTrN11UpdateDto>
{
}
