using System;
using System.Collections.Generic;
using Integration.Framework.Base.Dtos.Interfaces;
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

    public void RequestReload() => OnReloadRequested?.Invoke();

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

    public bool CanGoNext => ListDataSource != null && SelectedItem != null && ListDataSource.IndexOf(SelectedItem) < ListDataSource.Count - 1;
    public bool CanGoPrevious => ListDataSource != null && SelectedItem != null && ListDataSource.IndexOf(SelectedItem) > 0;

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
    {
        if (CanGoNext)
        {
            var currentIndex = ListDataSource.IndexOf(SelectedItem!);
            SetDataRowSelected(ListDataSource[currentIndex + 1]);
            // Not: Detay bilgisinin (GetDto) sunucudan tekrar çekilmesi CrudPageBase'deki OnSelectionChanged veya benzer bir event üzerinden tetiklenebilir.
        }
    }

    public virtual void GoPreviousRecord()
    {
        if (CanGoPrevious)
        {
            var currentIndex = ListDataSource.IndexOf(SelectedItem!);
            SetDataRowSelected(ListDataSource[currentIndex - 1]);
        }
    }
}
