using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.Framework.Blazor.Client.Services.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.AspNetCore.Components;
using Volo.Abp.Domain.Entities;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Tüm CRUD işlemlerini yürüten gelişmiş Base sayfa bileşeni.
/// </summary>
public abstract class CrudPageBase<TGetDto, TListDto, TKey, TListRequestDto, TCreateInput, TUpdateInput> 
    : AbpComponentBase, IDisposable
    where TGetDto : class, IGetDto<TKey>, new()
    where TListDto : class, IListDto<TKey>, new()
    where TListRequestDto : ListRequestDto, new()
    where TCreateInput : class, new()
    where TUpdateInput : class, new()
{
    // Alt sınıflar kendi spesifik servislerini (örn. ITenantAppService) buraya bağlamalıdır
    public abstract ICrudAppService<TGetDto, TListDto, TKey, TListRequestDto, TCreateInput, TUpdateInput> CrudAppService { get; }

    private GridListDataSource<TListDto>? _gridDataSource;

    /// <summary>
    /// Server-side grid veri kaynağı — CrudLayout'a <c>DataSource</c> olarak bağlanır.
    /// DxGrid'in paging/sort talebini nötr <see cref="ListRequestDto"/>'ya çevirip
    /// AppService'e gönderir; tüm veri kümesi belleğe çekilmez.
    /// </summary>
    public GridListDataSource<TListDto> GridDataSource
        => _gridDataSource ??= new GridListDataSource<TListDto>(FetchPageAsync) { OnError = HandleErrorAsync };

    private Task<PagedResultDto<TListDto>> FetchPageAsync(ListRequestDto request)
    {
        // Nötr sözleşmeyi alt sınıfın somut request tipine kopyala (o da ListRequestDto'dan türer).
        var typed = new TListRequestDto
        {
            SkipCount      = request.SkipCount,
            MaxResultCount = request.MaxResultCount,
            Sorting        = request.Sorting,
            Filter         = request.Filter,
            Sorts          = request.Sorts,
            Filters        = request.Filters,
            IsActive       = request.IsActive,
        };
        OnConfiguringListRequest(typed);
        return CrudAppService.GetListAsync(typed);
    }

    /// <summary>
    /// Alt sınıfların extra filtre eklemesine izin verir (örn. CompanyId, BranchId).
    /// FetchPageAsync her sayfa çekiminde çağırır.
    /// </summary>
    protected virtual void OnConfiguringListRequest(TListRequestDto request) { }

    [Inject]
    public ICrudStateService<TListDto, TKey> StateService { get; set; } = default!;

    [Inject]
    protected ITradeXpressUiService UiService { get; set; } = default!;

    [Inject]
    protected IPopupService PopupService { get; set; } = default!;

    public abstract Type EditComponentType { get; }

    // Konvansiyon: alt sınıf yalnızca PermissionPrefix verirse (örn. "AbpTenantManagement.Tenants")
    // üç policy adı otomatik türetilir. Gerekirse tek tek override edilebilir.
    protected virtual string? PermissionPrefix => null;

    protected virtual string? CreatePolicyName => PermissionPrefix is null ? null : $"{PermissionPrefix}.Create";
    protected virtual string? UpdatePolicyName => PermissionPrefix is null ? null : $"{PermissionPrefix}.Update";
    protected virtual string? DeletePolicyName => PermissionPrefix is null ? null : $"{PermissionPrefix}.Delete";

    [CascadingParameter(Name = "CurrentMdiTab")]
    public Integration.Framework.Blazor.Client.Services.Mdi.IMdiTab? CurrentMdiTab { get; set; }

    [Inject]
    protected Integration.Framework.Blazor.Client.Services.Mdi.IEntityChangeNotifier? EntityChanges { get; set; }

    /// <summary>Sekmeler arası değişim bildirimi için bu liste sayfasının entity anahtarı.
    /// Edit sekmesi aynı anahtarla <c>Notify</c> çağırınca bu liste grid'ini yeniler.
    /// Varsayılan: TListDto tam adı. Identity gibi adapter sayfalar sabit bir anahtarla override eder.</summary>
    protected virtual string EntityChangeKey => typeof(TListDto).FullName ?? typeof(TListDto).Name;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        StateService.OnStateChanged += OnStateChangedHandler;
        await SetPermissionsAsync();

        if (EntityChanges != null)
        {
            EntityChanges.EntityChanged += OnEntityChangedExternally;
        }

        if (CurrentMdiTab != null)
        {
            CurrentMdiTab.CanCloseAsync = CheckCanCloseAsync;
        }

        // Server-side: grid, GridDataSource üstünden ilk sayfayı kendi çeker (pre-fetch yok).
    }

    // Başka bir sekmede (ör. edit sekmesi) aynı entity değişince grid'i tazele.
    private void OnEntityChangedExternally(string key)
    {
        if (!string.Equals(key, EntityChangeKey, StringComparison.Ordinal)) return;
        InvokeAsync(() =>
        {
            StateService.RequestReload();
            StateHasChanged();
        });
    }

    protected virtual Task<bool> CheckCanCloseAsync()
    {
        return Task.FromResult(true);
    }

    protected virtual async Task SetPermissionsAsync()
    {
        // In Blazor Server mode ABP's principal accessor may deadlock the circuit's
        // SynchronizationContext (blocking .GetResult() on GetAuthenticationStateAsync).
        // Skip UI-level permission flags here — the server-side API still enforces them.
        if (!OperatingSystem.IsBrowser())
        {
            StateService.IsGrantedCreate = StateService.IsGrantedUpdate = StateService.IsGrantedDelete = true;
            return;
        }

        StateService.IsGrantedCreate = string.IsNullOrEmpty(CreatePolicyName) || await AuthorizationService.IsGrantedAsync(CreatePolicyName);
        StateService.IsGrantedUpdate = string.IsNullOrEmpty(UpdatePolicyName) || await AuthorizationService.IsGrantedAsync(UpdatePolicyName);
        StateService.IsGrantedDelete = string.IsNullOrEmpty(DeletePolicyName) || await AuthorizationService.IsGrantedAsync(DeletePolicyName);
    }

    protected async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            StateService.IsBusy = true;
            await action();
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex);
        }
        finally
        {
            StateService.IsBusy = false;
            StateService.NotifyStateChanged();
        }
    }

    /// <summary>Toolbar "Yenile" — grid'i sunucudan yeniden yükler.</summary>
    public virtual Task GetListAsync()
    {
        StateService.RequestReload();
        return Task.CompletedTask;
    }

    /// <summary>Toolbar arama kutusundan gelen metin — server-side global filtreye gider.</summary>
    public virtual Task OnSearchAsync(string text)
    {
        GridDataSource.SearchText = text;
        StateService.RequestReload();
        return Task.CompletedTask;
    }

    public virtual async Task BeforeCreateAsync()
    {
        StateService.SetDataRowSelected(null!);
        await ShowPopupAsync(default);
    }

    public virtual async Task BeforeUpdateAsync(TListDto entity)
    {
        StateService.SetDataRowSelected(entity);
        await ShowPopupAsync(entity.Id);
    }

    protected virtual async Task ShowPopupAsync(TKey? id)
    {
        var parameters = new System.Collections.Generic.Dictionary<string, object>
        {
            { "Id", id },
            { "IsPopupMode", true },
            { "OnSaved", Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, GetListAsync) },
            { "OnClosed", Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => PopupService.Close()) }
        };

        // If the component has an 'L' parameter, we can optionally try to pass our L, 
        // but it's better if the component uses its own inherited L or localization logic.

        await PopupService.ShowAsync(EditComponentType, parameters);
    }

    public virtual async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        string confirmMessage = selectedItems.Count == 1
            ? L["AreYouSureToDelete"]
            : string.Format(L["AreYouSureToDeleteMultiple"], selectedItems.Count);

        var dialogResult = await UiService.ConfirmDeleteAsync(confirmMessage);
        if (dialogResult != ConfirmDialogResult.Yes)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            // Copy selection list to avoid modification during enumeration
            var itemsToDelete = selectedItems.OfType<TListDto>().ToList();

            var result = await BatchOperation.ExecuteAsync(
                itemsToDelete,
                item => CrudAppService.DeleteAsync(item.Id));

            // Sadece silinemeyenler seçili kalsın; reload taze sunucu sayfasını çeker.
            StateService.SelectedDataItems = result.Failed
                .Select(f => (object)f.Item!)
                .ToList();

            StateService.RequestReload();
            ReportBatchDeleteResult(result);
        });
    }

    protected virtual void ReportBatchDeleteResult(BatchOperationResult<TListDto> result)
    {
        if (result.AllSucceeded)
        {
            UiService.ShowSuccessToast(L["SuccessfullyDeleted"]);
        }
        else if (result.IsPartial)
        {
            UiService.ShowWarningToast(
                string.Format(L["DeleteMultiplePartialSuccess"], result.Succeeded.Count, result.Failed.Count));
        }
        else if (result.HasFailures)
        {
            UiService.ShowErrorToast(L["DeleteMultipleAllFailed"]);
        }
    }



    // #3 (thread-affinity fix): StateHasChanged'i her zaman renderer dispatcher'ında çalıştır.
    // Arka plan kaynağı (SSE/timer/distributed event handler) state'i değiştirip NotifyStateChanged
    // çağırsa bile "current thread is not associated with the Dispatcher" hatası olmaz.
    // UI thread'inde zaten ucuz bir no-op hop'tur.
    private void OnStateChangedHandler() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        if (StateService != null)
        {
            StateService.OnStateChanged -= OnStateChangedHandler;
        }
        if (EntityChanges != null)
        {
            EntityChanges.EntityChanged -= OnEntityChangedExternally;
        }
    }
}
