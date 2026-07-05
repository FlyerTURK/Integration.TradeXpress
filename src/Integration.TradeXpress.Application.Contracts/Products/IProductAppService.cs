using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Products;

public interface IProductAppService : ICrudAppService<
    ProductGetDto,
    ProductListDto,
    Guid,
    ProductListRequestDto,
    ProductCreateDto,
    ProductUpdateDto>
{
}
