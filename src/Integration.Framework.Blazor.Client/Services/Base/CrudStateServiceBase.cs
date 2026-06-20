using System;
using System.Collections.Generic;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.Framework.Base.Querying;
using Integration.Framework.Blazor.Client.Components.Crud;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// Tüm CRUD UI operasyonlarında kullanılan State Servis Base sınıfı.
/// Gelişmiş Navigasyon özelliklerini (Next/Previous, IsDirty vb.) içerir.
/// </summary>
public abstract class CrudStateServiceBase<TListDto, TKey> : ICrudStateService<TListDto, TKey>
    where TListDto : class, IListDto<TKey>, new()
{
    public event Action? OnStateChanged;
    public event Action? OnReloadRequested;
    public event Func<TListDto, System.Threading.Tasks.Task>? OnFocusItemRequested;

    public void RequestReload() => OnReloadRequested?.Invoke();
    public System.Threading.Tasks.Task FocusGridItemAsync(TListDto item)
        => OnFocusItemRequested?.Invoke(item) ?? System.Threading.Tasks.Task.CompletedTask;

    // #1 (tutarsız bildirim fix): tüm UI-state mutasyonu guarded Set'ten geçer → değişiklikte
    // tek tip otomatik notify; değer aynıysa no-op (gereksiz render yok). "Notify'ı unutma" sınıfı kalktı.
    private bool _isLoaded;
    public bool IsLoaded { get => _isLoaded; set => Set(ref _isLoaded, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }



    private bool _isPopupListPage;
    public bool IsPopupListPage { get => _isPopupListPage; set => Set(ref _isPopupListPage, value); }

    private IList<TListDto> _listDataSource = new List<TListDto>();
    public IList<TListDto> ListDataSource { get => _listDataSource; set => Set(ref _listDataSource, value); }

    private TListDto? _selectedItem;
    public TListDto? SelectedItem
    {
        get => _selectedItem;
        set => Set(ref _selectedItem, value);
    }

    private IReadOnlyList<object> _selectedDataItems = new List<object>();
    public IReadOnlyList<object> SelectedDataItems
    {
        get => _selectedDataItems;
        set
        {
            // Çoklu-seçim + birincil seçili öğe senkronu tek mutasyon; sonunda bir kez notify.
            _selectedDataItems = value ?? new List<object>();
            _selectedItem = System.Linq.Enumerable.FirstOrDefault(_selectedDataItems) as TListDto;
            NotifyStateChanged();
        }
    }

    private bool _isGrantedCreate;
    public bool IsGrantedCreate { get => _isGrantedCreate; set => Set(ref _isGrantedCreate, value); }

    private bool _isGrantedUpdate;
    public bool IsGrantedUpdate { get => _isGrantedUpdate; set => Set(ref _isGrantedUpdate, value); }

    private bool _isGrantedDelete;
    public bool IsGrantedDelete { get => _isGrantedDelete; set => Set(ref _isGrantedDelete, value); }

    // Geçerli kaydın anahtarı (TKey constraint'siz → boxing ile object?'e çevir).
    private object? CurrentKey => SelectedItem is { } s ? s.Id : (object?)null;

    // Liste Id'leri (RecordNavigation object anahtar bekler). Id-bazlı arama → ListDataSource kopya
    // olsa bile (referans eşitliği yerine Id) doğru çalışır.
    private IReadOnlyList<object> KeyList()
    {
        var keys = new List<object>(ListDataSource?.Count ?? 0);
        if (ListDataSource != null)
            foreach (var item in ListDataSource)
            {
                if (item == null) continue;
                object? key = item.Id;
                if (key != null) keys.Add(key);
            }
        return keys;
    }

    public bool CanGoNext     => RecordNavigation.CanGoNext(KeyList(), CurrentKey);
    public bool CanGoPrevious => RecordNavigation.CanGoPrevious(KeyList(), CurrentKey);

    // ── Sayfa-aşırı gezinme durumu (grid fetch'te CrudLayout yazar) ──
    private long _totalCount;
    public long TotalCount { get => _totalCount; set => Set(ref _totalCount, value); }

    private int _pageSize = 20;
    public int PageSize { get => _pageSize; set => Set(ref _pageSize, value); }

    private int _pageSkip;
    public int PageSkip { get => _pageSkip; set => Set(ref _pageSkip, value); }

    private IReadOnlyList<SortField> _sorts = new List<SortField>();
    public IReadOnlyList<SortField> Sorts { get => _sorts; set => Set(ref _sorts, value); }

    private string? _filter;
    public string? Filter { get => _filter; set => Set(ref _filter, value); }

    private bool? _isActiveFilter;
    public bool? IsActiveFilter { get => _isActiveFilter; set => Set(ref _isActiveFilter, value); }

    /// <summary>Seçili kaydın tüm kayıtlar içindeki sırası (PageSkip + yüklü sayfadaki yerel index); yoksa -1.</summary>
    public int CurrentGlobalIndex
    {
        get
        {
            var local = RecordNavigation.IndexOf(KeyList(), CurrentKey);
            return local < 0 ? -1 : PageSkip + local;
        }
    }
    public bool CanGoPreviousGlobal => CurrentGlobalIndex > 0;
    public bool CanGoNextGlobal     => CurrentGlobalIndex >= 0 && CurrentGlobalIndex < TotalCount - 1;

    // Global index'teki tek kaydı çeken delege; CrudLayout grid kaynağına bağlar (popup sayfa-aşırı gezinme).
    public Func<int, System.Threading.Tasks.Task<TListDto?>>? FetchSingleByIndex { get; set; }

    public void NotifyStateChanged() => OnStateChanged?.Invoke();

    /// <summary>Guarded setter: değer değiştiyse alanı yazar ve bir kez notify eder; aynıysa no-op.
    /// Blazor bir senkron blok içindeki çoklu StateHasChanged'i tek render'a batch'ler → storm yok.</summary>
    private bool Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        NotifyStateChanged();
        return true;
    }



    public virtual void SetDataRowSelected(TListDto item)
    {
        _selectedItem = item;
        _selectedDataItems = item == null ? new List<object>() : new List<object> { item };
        NotifyStateChanged();
    }

    public virtual void GoNextRecord()
        => MoveToKey(RecordNavigation.NextKey(KeyList(), CurrentKey));

    public virtual void GoPreviousRecord()
        => MoveToKey(RecordNavigation.PreviousKey(KeyList(), CurrentKey));

    // Hedef Id'li kaydı listede bulup seçili yap (merkezi RecordNavigation komşu Id'yi döner).
    private void MoveToKey(object? targetId)
    {
        if (targetId == null || ListDataSource == null) return;
        var match = System.Linq.Enumerable.FirstOrDefault(ListDataSource, x => x != null && Equals(x.Id, targetId));
        if (match != null) SetDataRowSelected(match);
    }
}
