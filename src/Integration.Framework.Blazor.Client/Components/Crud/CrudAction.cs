using DevExpress.Blazor;
using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Edit formu footer'ındaki tek bir aksiyon (Save, Cancel, SaveAndClose, SaveAndNew, ...).
/// Deklaratif: ne göründüğü (Text/Icon/RenderStyle/Order), ne zaman aktif olduğu (CanExecute) ve
/// ne yaptığı (SubmitForm ile form gönder, ya da OnExecute) tek yerde tarif edilir. Toolbar yalnız
/// kayıtlı aksiyonları dizer. Sihir/convention YOK — düz, test edilebilir bir nesne.
/// </summary>
public sealed class CrudAction
{
    public string Text { get; set; } = string.Empty;
    public string? IconCssClass { get; set; }
    public ButtonRenderStyle RenderStyle { get; set; } = ButtonRenderStyle.Secondary;

    /// <summary>Footer içinde soldan sağa sıralama.</summary>
    public int Order { get; set; }

    /// <summary>true ise butona basınca EditForm submit edilir (validation + OnValidSubmit). Save için.</summary>
    public bool SubmitForm { get; set; }

    /// <summary>İşlem sürerken (IsBusy) buton içinde spinner göster (genelde Save).</summary>
    public bool ShowBusySpinner { get; set; }

    /// <summary>Aktiflik koşulu. null → daima aktif.</summary>
    public Func<bool>? CanExecute { get; set; }

    /// <summary>SubmitForm olmayan aksiyonların işi (Cancel, custom). Save için boş bırakılır (submit halleder).</summary>
    public EventCallback OnExecute { get; set; }
}
