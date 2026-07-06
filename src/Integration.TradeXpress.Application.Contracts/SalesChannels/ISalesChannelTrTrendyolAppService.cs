using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>Trendyol satış kanalı CRUD (tipe-özel) — generic <c>ICrudAppService</c>; company-owned. Liste tür-bağımsız
/// <see cref="ISalesChannelAppService"/>'te; burada Trendyol'a özel get/create/update (SellerId/ApiKey/ApiSecret).</summary>
public interface ISalesChannelTrTrendyolAppService : ICrudAppService<
    SalesChannelTrTrendyolGetDto,
    SalesChannelListDto,
    Guid,
    SalesChannelListRequestDto,
    SalesChannelTrTrendyolCreateDto,
    SalesChannelTrTrendyolUpdateDto>
{
}
