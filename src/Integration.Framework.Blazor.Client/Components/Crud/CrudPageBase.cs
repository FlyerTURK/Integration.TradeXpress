using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Tüm CRUD işlemlerini yürüten gelişmiş Base sayfa bileşeni.
/// </summary>
public abstract class CrudPageBase<TGetDto, TListDto, TKey, TListRequestDto, TCreateInput, TUpdateInput>
    : AbpComponentBase, IDisposable, ISplitListActions
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
        // DevExpress grid veri callback'i ARKA PLAN thread'inde çalışır → hata yönetimini Dispatcher'a marshal et,
        // yoksa HandleErrorAsync.StateHasChanged "thread not associated with the Dispatcher" ile çöker (gerçek hatayı maskeler).
        => _gridDataSource ??= new GridListDataSource<TListDto>(FetchPageAsync)
        { OnError = ex => InvokeAsync(() => HandleErrorAsync(ex)) };

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
    protected IUiInteractionService UiService { get; set; } = default!;

    [Inject]
    protected IPopupService PopupService { get; set; } = default!;

    [Inject]
    protected IViewOpener ViewOpener { get; set; } = default!;

    public abstract Type EditComponentType { get; }

    /// <summary>Popup/sekme başlığı — ViewOpener'a geçilir. Override edilebilir.
    /// Varsayılan: TGetDto adından "GetDto" suffix'i kırpılmış lokalizasyon key'i.</summary>
    protected virtual string EditTitle => CrudNaming.EntityCaption(typeof(TGetDto), L);
    protected virtual string? EditIconCssClass => null;

    // Konvansiyon: alt sınıf yalnızca PermissionPrefix verirse (örn. "AbpTenantManagement.Tenants")
    // üç policy adı otomatik türetilir. Gerekirse tek tek override edilebilir.
    protected virtual string? PermissionPrefix => null;

    protected virtual string? CreatePolicyName => PermissionPrefix is null ? null : $"{PermissionPrefix}.Create";
    protected virtual string? UpdatePolicyName => PermissionPrefix is null ? null : $"{PermissionPrefix}.Update";
    protected virtual string? DeletePolicyName => PermissionPrefix is null ? null : $"{PermissionPrefix}.Delete";

    [CascadingParameter(Name = "CurrentMdiTab")]
    public Integration.Framework.Blazor.Client.Services.Mdi.IMdiTab? CurrentMdiTab { get; set; }

    /// <summary>
    /// SplitCrudView birleşik toolbar + seçim host'u. Doluysa bu liste kendi toolbar'ını çizmez
    /// (CrudLayout aynı cascade'i görüp gizler), aksiyonlarını host'a register eder ve
    /// satır seçimleri popup yerine host'un guard'lı geçişlerine gider.
    /// </summary>
    [CascadingParameter]
    public Integration.Framework.Blazor.Client.Services.Base.ISplitHost? SplitHost { get; set; }

    // ── ISplitListActions ──
    bool ISplitListActions.CanCreate   => StateService.IsGrantedCreate;
    bool ISplitListActions.CanDelete   => StateService.IsGrantedDelete;
    bool ISplitListActions.HasSelection => StateService.SelectedDataItems is { Count: > 0 };
    Task ISplitListActions.NewAsync()    => BeforeCreateAsync();
    Task ISplitListActions.RefreshAsync() => GetListAsync();
    // DeleteAsync() zaten public — ISplitListActions.DeleteAsync'i otomatik karşılar.

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
        SplitHost?.RegisterList(this);
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
        if (SplitHost != null)
            await SplitHost.RequestNewAsync();       // guard'lı: açık edit kirliyse önce onay
        else
            await ShowPopupAsync(default);
    }

    public virtual async Task BeforeUpdateAsync(TListDto entity)
    {
        StateService.SetDataRowSelected(entity);
        // Not: Gezinme durumu (ListDataSource/TotalCount/PageSkip) PAYLAŞILAN StateService'e zaten grid'in
        // Fetched senkronundan (CrudLayout.SyncStateFromGrid) yazılır; burada ayrıca yazmaya gerek yok.
        if (SplitHost != null)
            await SplitHost.RequestSelectAsync(entity.Id);   // guard'lı geçiş
        else
            await ShowPopupAsync(entity.Id);
    }

    /// <summary>Edit formunun nerede açılacağı (Popup/MDI sekmesi). OTOMATİK: liste bir MDI sekmesinde açıldıysa
    /// edit de sekmede açılır (kullanıcı tercihi); değilse popup. Edit bileşeninin @page route'u yoksa
    /// (BuildEditUrl null) zaten popup'a düşülür. Alt sınıf gerekirse override eder (ör. zorla Popup).</summary>
    protected virtual EditOpenTarget EditOpenTarget => CurrentMdiTab != null ? EditOpenTarget.MdiTab : EditOpenTarget.Popup;

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // MDI sekme açıcı — uygulama sağlar (TabManager); kayıtlı değilse null → popup'a düşülür (framework app tipine bağımlı değil).
    private Integration.Framework.Blazor.Client.Services.Mdi.IMdiTabOpener? TabOpener
        => ServiceProvider.GetService(typeof(Integration.Framework.Blazor.Client.Services.Mdi.IMdiTabOpener))
           as Integration.Framework.Blazor.Client.Services.Mdi.IMdiTabOpener;

    /// <summary>Alt sınıf, edit bileşenine EK parametre geçirebilir (ör. drill-down bağlamı: BranchId/CompanyId).
    /// Varsayılan null → değişiklik yok. ViewOpener'ın extra param'larına merge edilir.</summary>
    protected virtual System.Collections.Generic.Dictionary<string, object>? AdditionalEditParameters => null;

    protected virtual Task ShowPopupAsync(TKey? id)
    {
        // Sayfa MDI sekmesi istiyorsa ve uygulama MDI sağlıyorsa: edit page'in route'unu sekmede aç (popup yerine).
        if (EditOpenTarget == EditOpenTarget.MdiTab && TabOpener is { } tabs && BuildEditUrl(id) is { } url)
            return tabs.OpenOrActivateAsync(url, EditTitle, EditIconCssClass);

        // Nav bağlamı artık PAYLAŞILAN StateService'ten okunuyor (AddScoped) — parametre geçmeye gerek yok.
        var extra = new System.Collections.Generic.Dictionary<string, object>
        {
            { "OnSaved",  Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, GetListAsync) },
            { "OnClosed", Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => PopupService.Close()) },
        };

        // Alt sınıf bağlam parametreleri (ör. BranchId) — edit bileşenine geçir.
        if (AdditionalEditParameters is { } more)
            foreach (var kv in more)
                extra[kv.Key] = kv.Value;

        if (BuildEditUrl(id) is { } tabUrl)
            extra["_TabUrl"] = tabUrl;

        // Chrome başlığı boş — yapısal başlık popup gövdesinde gösterilir.
        return ViewOpener.OpenAsync(EditComponentType, id, string.Empty, EditIconCssClass, extra);
    }

    // EditComponentType'ın @page route'undan sekme URL'i kurar: id yoksa parametresiz ("new") route, varsa
    // tek {param}'lı route'ta param'ı id ile değiştirir. (Edit page'leri tek anahtarlı route kullanır.)
    private string? BuildEditUrl(TKey? id)
    {
        var templates = EditComponentType
            .GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>()
            .Select(r => r.Template)
            .ToList();
        if (templates.Count == 0) return null;

        bool isNew = id is null || id.Equals(default(TKey));
        string? url = null;
        if (isNew)
            url = templates.FirstOrDefault(t => !t.Contains('{'));
        else
        {
            var paramT = templates.FirstOrDefault(t => t.Contains('{'));
            if (paramT != null)
            {
                var open  = paramT.IndexOf('{');
                var close = paramT.IndexOf('}', open);
                if (close >= 0) url = paramT[..open] + id + paramT[(close + 1)..];
            }
        }

        if (url != null && AdditionalEditParameters?.Count > 0)
        {
            var qs = string.Join("&", AdditionalEditParameters.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value?.ToString() ?? "")}"));
            url = $"{url}?{qs}";
        }
        return url;
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

        // İki butonlu onay: "Kaydı Sil / Kayıtları Sil" + "Vazgeç" (No yok; güvenli varsayılan Vazgeç).
        string deleteButtonText = selectedItems.Count == 1
            ? L["DeleteRecordButton"]
            : L["DeleteRecordsButton"];

        var dialogResult = await UiService.ConfirmDeleteAsync(confirmMessage, yesText: deleteButtonText);
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
    private void OnStateChangedHandler()
    {
        SplitHost?.NotifyChanged();   // seçim değişince birleşik toolbar Sil butonunu güncelle
        InvokeAsync(StateHasChanged);
    }

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
