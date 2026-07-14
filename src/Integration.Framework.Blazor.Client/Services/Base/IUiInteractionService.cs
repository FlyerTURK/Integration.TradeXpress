using Volo.Abp.DependencyInjection;

namespace Integration.Framework.Blazor.Client.Services.Base;

public enum ConfirmDialogResult
{
    Yes,
    No,
    Cancel
}

public interface IUiInteractionService : IScopedDependency
{
    event Action? OnDialogChanged;
    bool IsDialogVisible { get; }
    string? DialogMessage { get; }
    string? DialogTitle { get; }
    /// <summary>Onay (Yes) butonu metni — null ise varsayılan "Evet".</summary>
    string? DialogYesText { get; }
    /// <summary>Ret (No) butonu metni — null ise varsayılan "Hayır".</summary>
    string? DialogNoText { get; }
    /// <summary>Cancel butonu gösterilsin mi? false ise iptal görevini yalnız pencerenin çarpısı yapar.</summary>
    bool DialogShowCancel { get; }
    /// <summary>No butonu gösterilsin mi? Silme onayı gibi iki-butonlu ("Sil" + "Vazgeç") diyaloglar false verir.</summary>
    bool DialogShowNo { get; }
    /// <summary>Varsayılan (primary + odaklı) buton Yes mi? true → Yes, false → No (No gizliyse Cancel). Silme'de güvenli olan Vazgeç, dirty'de Kaydet (Yes).</summary>
    bool DialogDefaultYes { get; }

    /// <summary>Diyalogda serbest metin girişi gösterilsin mi (ör. red gerekçesi) — <see cref="PromptAsync"/> true verir,
    /// düz <see cref="ConfirmAsync"/>/<see cref="ConfirmDeleteAsync"/> false.</summary>
    bool DialogShowInput { get; }
    /// <summary>Girdi alanının placeholder/etiketi.</summary>
    string? DialogInputLabel { get; }
    /// <summary>Girdi değeri — kullanıcı yazdıkça diyalog bileşeni günceller (iki yönlü).</summary>
    string? DialogInputValue { get; set; }
    /// <summary>Girdi boşken Evet devre dışı mı (zorunlu alan)?</summary>
    bool DialogInputRequired { get; }

    /// <summary>Silme onayı: "<paramref name="yesText"/> (Kaydı/Kayıtları Sil)" + "Vazgeç" — No butonu YOK.</summary>
    Task<ConfirmDialogResult> ConfirmDeleteAsync(string message, string? title = null, string? yesText = null);
    /// <summary>Genel onay diyaloğu — özel buton metinleri, Cancel/No gizleme ve varsayılan buton seçimiyle.</summary>
    Task<ConfirmDialogResult> ConfirmAsync(string message, string? title, string? yesText, string? noText, bool showCancel, bool defaultYes = false, bool showNo = true);
    /// <summary>Onay + serbest metin girişi TEK diyalogda (ör. "Bu siparişi reddet" + red gerekçesi) — kullanıcı
    /// gerçekten SORULUR, ayrı bir form alanını önceden doldurması beklenmez. <paramref name="inputRequired"/> true
    /// ise girdi boşken Evet devre dışı. Yes → (Yes, girilen metin); No/Cancel/kapatma → (sonuç, null).</summary>
    Task<(ConfirmDialogResult Result, string? Text)> PromptAsync(
        string message, string? title, string inputLabel, string yesText, string? noText,
        bool showCancel, bool inputRequired = true, string? initialValue = null, bool showNo = true);
    void CloseDialog(ConfirmDialogResult result);

    void ShowSuccessToast(string message);
    void ShowErrorToast(string message);
    void ShowWarningToast(string message);
}
