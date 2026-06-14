namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>Edit formunun nasıl gösterileceği — yerine göre parametreyle seçilir.</summary>
public enum EditViewMode
{
    /// <summary>Sayfa içinde (inline) gösterilir.</summary>
    Page,

    /// <summary>Popup (modal) içinde gösterilir.</summary>
    Popup
}
