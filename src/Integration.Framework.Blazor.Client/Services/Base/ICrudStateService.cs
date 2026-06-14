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
public interface ICrudStateService<TGetDto, TListDto, TKey, TViewModel> : Volo.Abp.DependencyInjection.IScopedDependency
    where TGetDto : class, IGetDto<TKey>, new()
    where TListDto : class, IListDto<TKey>, new()
    where TViewModel : class, IViewModel<TKey>, new()
{
    event Action? OnStateChanged;

    bool IsLoaded { get; set; }
    bool IsBusy { get; set; }
    
    // Popup veya Sayfa Görünürlük Durumları
    bool EditPageVisible { get; set; }
    bool IsPopupListPage { get; set; }

    // Grid Verisi
    IList<TListDto> ListDataSource { get; set; }
    
    // Grid'de Seçili Satır (List Dto)
    TListDto? SelectedItem { get; set; }

    // Grid'de Seçili Satırlar (Çoklu Seçim)
    IReadOnlyList<object> SelectedDataItems { get; set; }
    
    // Formun Doğrudan Bağlandığı Düzenleme Modeli (TViewModel)
    TViewModel? EditingModel { get; set; }
    
    // Form Navigasyon ve Durum İzleme
    bool IsDirty { get; set; }
    bool IsNewRecord { get; set; }
    bool CanGoNext { get; }
    bool CanGoPrevious { get; }

    // Yetki Kontrolleri (Türkan Şoray Kural 17)
    bool IsGrantedCreate { get; set; }
    bool IsGrantedUpdate { get; set; }
    bool IsGrantedDelete { get; set; }

    // Server-side grid: sayfa, grid'in sunucudan yeniden yüklenmesini ister; CrudLayout dinler.
    event Action? OnReloadRequested;
    void RequestReload();

    // Son kaydetme denemesinden gelen sunucu doğrulama hataları — CrudEditModal'ın ValidationMessageStore'a aktarması için.
    IReadOnlyList<ServerValidationError>? PendingServerErrors { get; set; }

    // Methodlar
    void NotifyStateChanged();
    void ShowEditPage(bool isNewRecord);
    void HideEditPage();
    void SetDataRowSelected(TListDto item);

    void GoNextRecord();
    void GoPreviousRecord();
}

/// <summary>Sunucudan dönen tek bir doğrulama hatası. MemberName null ise model-düzeyinde hata.</summary>
public record ServerValidationError(string? MemberName, string Message);
