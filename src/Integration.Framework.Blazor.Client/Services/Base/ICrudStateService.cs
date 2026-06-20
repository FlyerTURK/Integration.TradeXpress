using System;
using System.Collections.Generic;
using Integration.Framework.Base.Dtos.Interfaces;
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
    
    bool CanGoNext { get; }
    bool CanGoPrevious { get; }

    // Yetki Kontrolleri (Türkan Şoray Kural 17)
    bool IsGrantedCreate { get; set; }
    bool IsGrantedUpdate { get; set; }
    bool IsGrantedDelete { get; set; }

    // Server-side grid: sayfa, grid'in sunucudan yeniden yüklenmesini ister; CrudLayout dinler.
    event Action? OnReloadRequested;
    void RequestReload();

    // Methodlar
    void NotifyStateChanged();
    void SetDataRowSelected(TListDto item);

    void GoNextRecord();
    void GoPreviousRecord();
}
