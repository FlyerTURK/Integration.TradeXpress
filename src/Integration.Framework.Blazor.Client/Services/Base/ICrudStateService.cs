using System;
using System.Collections.Generic;
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

    /// <summary>Global index'teki TEK kaydı (grid'in aktif sıralaması/filtresiyle) çeker — sayfa-aşırı popup
    /// gezinmede komşu kaydı bulmak için. CrudLayout grid kaynağına (GridListDataSource.FetchSingleAsync) bağlar.</summary>
    Func<int, System.Threading.Tasks.Task<TListDto?>>? FetchSingleByIndex { get; set; }

    // Yetki Kontrolleri (Türkan Şoray Kural 17)
    bool IsGrantedCreate { get; set; }
    bool IsGrantedUpdate { get; set; }
    bool IsGrantedDelete { get; set; }

    // Server-side grid: sayfa, grid'in sunucudan yeniden yüklenmesini ister; CrudLayout dinler.
    event Action? OnReloadRequested;
    void RequestReload();

    // Popup/standalone edit komşu kayda gezinince grid'i o kaydın SAYFASINA götürüp odaklamasını ister.
    // CrudLayout, DevExpress Grid.SetFocusedDataItemAsync(item)'a bağlar → item farklı sayfadaysa grid
    // OTOMATİK o sayfaya gider + satırı odaklar (manuel PageIndex hesabı yok → popup arkasında da çalışır).
    event Func<TListDto, System.Threading.Tasks.Task>? OnFocusItemRequested;
    System.Threading.Tasks.Task FocusGridItemAsync(TListDto item);

    // Methodlar
    void NotifyStateChanged();
    void SetDataRowSelected(TListDto item);

    void GoNextRecord();
    void GoPreviousRecord();
}
