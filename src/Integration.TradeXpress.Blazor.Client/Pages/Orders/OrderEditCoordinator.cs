using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Orders;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Blazor.Client.Pages.Orders;

/// <summary>
/// Order için <see cref="ICommitCoordinator{TGetDto,TListDto,TKey,TListRequestDto}"/> — <c>PersistentCoordinator</c>
/// KULLANILAMAZ (IOrderAppService standart <c>ICrudAppService</c> şekli değil; Order'da Create/Delete YOK, yalnız
/// senkronizasyondan gelir). <see cref="NewModel"/>/<see cref="DeleteAsync"/> hiç ÇAĞRILMAZ — OrderEditHost
/// SupportsSaveAndNew/SupportsDelete=false verir, toolbar bu aksiyonları GİZLER.
/// </summary>
public class OrderEditCoordinator : ICommitCoordinator<OrderDto, OrderListDto, Guid, OrderListRequestDto>
{
    private readonly IOrderAppService _orderAppService;

    public OrderEditCoordinator(IOrderAppService orderAppService)
    {
        _orderAppService = orderAppService;
    }

    public OrderDto NewModel()
        => throw new NotSupportedException("Order elle oluşturulamaz — yalnız pazaryeri senkronizasyonundan gelir.");

    public Task<OrderDto> GetForEditAsync(Guid id) => _orderAppService.GetAsync(id);

    public Task<OrderDto> CommitAsync(OrderDto model) => _orderAppService.UpdateAsync(model.Id, model);

    public Task DeleteAsync(Guid id)
        => throw new NotSupportedException("Order elle silinemez — yalnız pazaryeri senkronizasyonundan gelir.");

    public Task<PagedResultDto<OrderListDto>> FetchAsync(OrderListRequestDto request) => _orderAppService.GetListAsync(request);

    public bool CanGoPrevious => false;
    public bool CanGoNext => false;
    public Task<OrderDto?> GoPreviousAsync() => Task.FromResult<OrderDto?>(null);
    public Task<OrderDto?> GoNextAsync() => Task.FromResult<OrderDto?>(null);
}
