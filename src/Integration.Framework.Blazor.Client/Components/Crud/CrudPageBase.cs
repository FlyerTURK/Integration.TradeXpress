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
using Volo.Abp.Http.Client;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Tüm CRUD işlemlerini yürüten gelişmiş Base sayfa bileşeni.
/// </summary>
public abstract class CrudPageBase<TGetDto, TListDto, TKey, TListRequestDto, TCreateInput, TUpdateInput, TViewModel> 
    : AbpComponentBase, IDisposable
    where TGetDto : class, IGetDto<TKey>, new()
    where TListDto : class, IListDto<TKey>, new()
    where TListRequestDto : ListRequestDto, new()
    where TCreateInput : class, new()
    where TUpdateInput : class, new()
    where TViewModel : class, IViewModel<TKey>, new()
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
    public ICrudStateService<TGetDto, TListDto, TKey, TViewModel> StateService { get; set; } = default!;

    [Inject]
    protected ITradeXpressUiService UiService { get; set; } = default!;

    // Konvansiyon: alt sınıf yalnızca PermissionPrefix verirse (örn. "AbpTenantManagement.Tenants")
    // üç policy adı otomatik türetilir. Gerekirse tek tek override edilebilir.
    protected virtual string? PermissionPrefix => null;

    protected virtual string? CreatePolicyName => PermissionPrefix is null ? null : $"{PermissionPrefix}.Create";
    protected virtual string? UpdatePolicyName => PermissionPrefix is null ? null : $"{PermissionPrefix}.Update";
    protected virtual string? DeletePolicyName => PermissionPrefix is null ? null : $"{PermissionPrefix}.Delete";

    [CascadingParameter(Name = "CurrentMdiTab")]
    public Integration.Framework.Blazor.Client.Services.Mdi.IMdiTab? CurrentMdiTab { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        StateService.OnStateChanged += OnStateChangedHandler;
        await SetPermissionsAsync();
        
        if (CurrentMdiTab != null)
        {
            CurrentMdiTab.CanCloseAsync = CheckCanCloseAsync;
        }
        
        // Server-side: grid, GridDataSource üstünden ilk sayfayı kendi çeker (pre-fetch yok).
    }

    protected virtual async Task<bool> CheckCanCloseAsync()
    {
        if (StateService.IsDirty)
        {
            var dialogResult = await UiService.ConfirmDeleteAsync(L["DiscardChangesConfirmation"]);
            if (dialogResult != ConfirmDialogResult.Yes)
            {
                return false; // Kullanıcı iptal etti, sekmeyi kapatma
            }
        }
        return true; // Temiz veya kullanıcı çıkmayı onayladı
    }

    protected virtual async Task SetPermissionsAsync()
    {
        StateService.IsGrantedCreate = string.IsNullOrEmpty(CreatePolicyName) || await AuthorizationService.IsGrantedAsync(CreatePolicyName);
        StateService.IsGrantedUpdate = string.IsNullOrEmpty(UpdatePolicyName) || await AuthorizationService.IsGrantedAsync(UpdatePolicyName);
        StateService.IsGrantedDelete = string.IsNullOrEmpty(DeletePolicyName) || await AuthorizationService.IsGrantedAsync(DeletePolicyName);
    }

    protected async Task ExecuteAsync(Func<Task> action)
    {
        StateService.PendingServerErrors = null;
        try
        {
            StateService.IsBusy = true;
            await action();
        }
        catch (AbpRemoteCallException ex) when (ex.Error?.ValidationErrors?.Length > 0)
        {
            // Validation hataları toast yerine form alanlarına aktar; CrudEditModal HandleValidSubmit'te okur.
            StateService.PendingServerErrors = ex.Error.ValidationErrors
                .SelectMany(e => e.Members is { Length: > 0 }
                    ? e.Members.Select(m => new ServerValidationError(m, e.Message ?? string.Empty))
                    : [new ServerValidationError(null, e.Message ?? string.Empty)])
                .ToList();
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

    public virtual Task BeforeCreateAsync()
    {
        StateService.EditingModel = new TViewModel();
        StateService.ShowEditPage(isNewRecord: true);
        return Task.CompletedTask;
    }

    public virtual async Task BeforeUpdateAsync(TListDto entity)
    {
        StateService.SetDataRowSelected(entity);
        await ExecuteAsync(async () =>
        {
            TGetDto fullEntity;
            try
            {
                fullEntity = await CrudAppService.GetAsync(entity.Id);
            }
            catch (EntityNotFoundException)
            {
                UiService.ShowWarningToast(L["RecordDeletedByAnotherUser"]);
                StateService.RequestReload();
                return;
            }
            catch (AbpRemoteCallException ex) when (ex.HttpStatusCode == 404)
            {
                UiService.ShowWarningToast(L["RecordDeletedByAnotherUser"]);
                StateService.RequestReload();
                return;
            }
            StateService.EditingModel = ObjectMapper.Map<TGetDto, TViewModel>(fullEntity);
            StateService.ShowEditPage(isNewRecord: false);
        });
    }

    public virtual async Task CreateAsync()
    {
        await ExecuteAsync(async () =>
        {
            var createInput = ObjectMapper.Map<TViewModel, TCreateInput>(StateService.EditingModel!);
            await CrudAppService.CreateAsync(createInput);
            StateService.HideEditPage();
            StateService.RequestReload();
            await Notify.Success(L["SuccessfullySaved"]);
        });
    }

    public virtual async Task UpdateAsync()
    {
        await ExecuteAsync(async () =>
        {
            var updateInput = ObjectMapper.Map<TViewModel, TUpdateInput>(StateService.EditingModel!);
            await CrudAppService.UpdateAsync(StateService.EditingModel!.Id, updateInput);

            StateService.HideEditPage();
            StateService.RequestReload();
            await Notify.Success(L["SuccessfullySaved"]);
        });
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

    public virtual async Task CancelEditAsync()
    {
        if (StateService.IsDirty)
        {
            var dialogResult = await UiService.ConfirmDeleteAsync(L["DiscardChangesConfirmation"]);
            if (dialogResult != ConfirmDialogResult.Yes)
            {
                return;
            }
        }
        StateService.HideEditPage();
    }

    public virtual async Task SaveAsync()
    {
        if (StateService.IsNewRecord)
        {
            await CreateAsync();
        }
        else
        {
            await UpdateAsync();
        }
    }

    /// <summary>Kaydet ve Yeni: kaydeder, başarılıysa (popup kapandıysa) hemen yeni kayıt moduna geçer.
    /// SaveAsync başarısız/validation'da popup açık kalır → yeni mod'a geçilmez. Page'ler SaveAsync ve
    /// BeforeCreateAsync'i override ettiğinden bu kompozisyon tüm entity'lerde çalışır.</summary>
    public virtual async Task SaveAndNewAsync()
    {
        await SaveAsync();
        if (!StateService.EditPageVisible)
            await BeforeCreateAsync();
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
    }
}
