using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parite yönetimi (CRUD). Görünürlük host‖own (tenant global'i salt-okur, kendi paritesini ekler).
/// Çift = base/quote; ters yön (USDTRY varken TRYUSD) reddedilir.
/// </summary>
public interface IParityAppService : ICrudAppService<
    ParityGetDto,
    ParityListDto,
    Guid,
    ParityListRequestDto,
    ParityCreateDto,
    ParityUpdateDto>
{
}
