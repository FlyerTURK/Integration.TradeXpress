using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Integration.Framework.Blazor.Client.Services.Base;

public class PopupOptions
{
    public string? Title { get; set; }
    public string? Width { get; set; } = "800px";
    public bool CloseOnEscape { get; set; } = false;
    public bool CloseOnOutsideClick { get; set; } = false;
}

public interface IPopupService
{
    event Action? OnShow;
    event Action? OnHide;

    Type? ComponentType { get; }
    Dictionary<string, object>? ComponentParameters { get; }
    PopupOptions? Options { get; }
    bool IsVisible { get; }

    /// <summary>
    /// Kapatmadan önce çalıştırılan opsiyonel onay/guard. false dönerse popup KAPATILMAZ
    /// (ör. kaydedilmemiş değişiklik varken "Vazgeçilsin mi?" onayı). İçerik bileşeni set eder;
    /// Show/Close sıfırlar.
    /// </summary>
    Func<Task<bool>>? CloseGuard { get; set; }

    Task<object?> ShowAsync(Type componentType, Dictionary<string, object>? parameters = null, PopupOptions? options = null);
    void Close(object? result = null);
}
