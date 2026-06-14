using System;
using System.Threading.Tasks;
using DevExpress.Blazor;

namespace Integration.TradeXpress.Blazor.Client.Theming;

/// <summary>
/// Seçili DevExpress <see cref="SizeMode"/> (Small/Medium/Large) değerini
/// localStorage'da saklar ve değişiklik olayını yayar; böylece
/// <c>Routes.razor</c>'daki cascading value tüm tüketicileri yeniden render eder.
/// </summary>
public interface ISizeModeService
{
    SizeMode CurrentSizeMode { get; }

    /// <summary>localStorage'dan kayıtlı boyut modunu okur (açılışta bir kez).</summary>
    Task InitializeAsync();

    Task SetAsync(SizeMode sizeMode);

    event EventHandler? SizeModeChanged;
}
