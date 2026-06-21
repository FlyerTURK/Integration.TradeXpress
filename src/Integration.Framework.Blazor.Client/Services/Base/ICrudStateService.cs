using Integration.Framework.Base.Dtos.Interfaces;
using Integration.Framework.Base.Querying;
using Integration.Framework.Blazor.Client.Components.Crud;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// Tüm CRUD UI operasyonlarında kullanılan State Servis Arayüzü.
/// DTO Kısıtlamaları ve Ayrımı standartlarına uyar.
/// Gelişmiş Navigasyon (Next, Previous, IsDirty vb.) yeteneklerini barındırır.
/// </summary>
public interface ICrudStateService<TListDto, TKey> : Volo.Abp.DependencyInjection.IScopedDependency
    where TListDto : class, IListDto<TKey>, new()
{
    event Action? OnStateChanged;

    bool IsLoaded { get; set; }
    bool IsBusy { get; set; }
    
    bool IsPopupListPage { get; set; }

    // Grid Verisi
    IList<TListDto> ListDataSource { get; set; }
    
    // Grid'de Seçili Satır (List Dto)
    TListDto? SelectedItem { get; set; }

    // Grid'de Seçili Satırlar (Çoklu Seçim)
    IReadOnlyList<object> SelectedDataItems { get; set; }
    
    // Yüklü-sayfa içi gezinme (mevcut hızlı yol).
    bool CanGoNext { get; }
    bool CanGoPrevious { get; }

    // ── Sayfa-aşırı (server-side, tüm kayıtlar) gezinme durumu ──
    // CrudLayout grid her fetch'te (GridListDataSource.Fetched) bu alanları yazar.
    long TotalCount { get; set; }                 // sunucudaki toplam kayıt
    int PageSize { get; set; }                    // grid sayfa boyutu
    int PageSkip { get; set; }                    // yüklü sayfanın SkipCount'u
    IReadOnlyList<SortField> Sorts { get; set; }  // aktif sıralama (tek-kayıt sorgusu birebir aynı sırada gitsin)
    string? Filter { get; set; }                  // aktif arama metni
    bool? IsActiveFilter { get; set; }            // aktif IsActive filtresi

    /// <summary>Seçili kaydın TÜM kayıtlar içindeki sırası (PageSkip + yüklü sayfadaki yerel index); yoksa -1.</summary>
    int CurrentGlobalIndex { get; }
    bool CanGoPreviousGlobal { get; }
    bool CanGoNextGlobal { get; }

    // Yetki Kontrolleri (Türkan Şoray Kural 17)
    bool IsGrantedCreate { get; set; }
    bool IsGrantedUpdate { get; set; }
    bool IsGrantedDelete { get; set; }

    // Server-side grid: sayfa, grid'in sunucudan yeniden yüklenmesini ister; CrudLayout dinler.
    event Action? OnReloadRequested;
    void RequestReload();

    // ── Köprü: liste grid'ini StateService'e doğrudan bağla (split + popup TEK prensiple gezinir) ──
    // CrudLayout her zaman (SplitHost'tan bağımsız) kendini register eder; köprü grid'i doğrudan sürer
    // (GridVisibleKeys/PageSkip/TotalCount canlı okunur, EnsurePageForGlobalIndexAsync/FocusDataItemAsync çağrılır).
    void RegisterGrid(ISplitGridActions grid);
    void UnregisterGrid(ISplitGridActions grid);

    /// <summary>"Geçerli kayıt" anahtarı kaynağı (popup: () => Id; null ise SelectedItem.Id). CurrentGlobalIndex
    /// bunu canlı kaynak alır → popup'ın ayrı stale index'i kalkar, tek tanım.</summary>
    Func<object?>? CurrentKeyProvider { get; set; }

    /// <summary>Komşu kayda geçmeden önce ayrılma güvenli mi? (popup: ConfirmCloseAsync — dirty Kaydet/Yoksay.)</summary>
    Func<System.Threading.Tasks.Task<bool>>? CanLeaveGuard { get; set; }

    /// <summary>Köprü hedef kaydı bulunca "nasıl gösterileceği" hook'u (popup: Id=Key; LoadDataAsync).</summary>
    Func<NavTransition, System.Threading.Tasks.Task>? OnRecordActivated { get; set; }

    /// <summary>Sayfa-aşırı (tüm kayıtlar) önceki/sonraki kayda geç: CrossPageNavigator + guard + grid + hook.</summary>
    System.Threading.Tasks.Task GoNextGlobalAsync();
    System.Threading.Tasks.Task GoPreviousGlobalAsync();

    // Methodlar
    void NotifyStateChanged();
    void SetDataRowSelected(TListDto item);

    void GoNextRecord();
    void GoPreviousRecord();
}
