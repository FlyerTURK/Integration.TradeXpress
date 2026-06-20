using Integration.Framework.Base.Dtos.Interfaces;
using Integration.Framework.Blazor.Client.Components.Crud;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// Özel davranış gerektirmeyen sayfalar için hazır StateService implementasyonu.
/// IntegrationFrameworkBlazorClientModule open generic olarak kaydeder —
/// ICrudStateService kullanan her sayfa ayrı bir sınıf yazmadan otomatik çözümlenir.
/// </summary>
public class DefaultCrudStateService<TListDto, TKey>
    : CrudStateServiceBase<TListDto, TKey>
    where TListDto : class, IListDto<TKey>, new()
{
}
