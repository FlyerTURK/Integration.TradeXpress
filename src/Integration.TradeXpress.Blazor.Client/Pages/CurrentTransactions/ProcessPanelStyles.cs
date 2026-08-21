namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Süreç panellerinin ortak inline stil string'leri — 12 dosyada tekrar eden
/// Group/Control/Label stillerinin TEK kaynağı (SSOT; CSS dosyası bilinçli olarak açılmadı).
/// </summary>
public static class ProcessPanelStyles
{
    /// <summary>Grup etiketi (small başlık) stili.</summary>
    public const string Label = "font-weight:600; text-transform:uppercase; letter-spacing:0.05em;";

    /// <summary>Grup sütunu div'inin stili: masaüstünde sabit genişlik (default 120px) + flex-shrink:0,
    /// mobilde tam genişlik. Metal gibi paneller alan bazında farklı genişlik (60/240px) geçer.</summary>
    public static string Group(bool isMobile, int width = 120)
        => "display:flex; flex-direction:column; gap:4px; " + (isMobile ? "width:100%;" : $"width:{width}px; flex-shrink:0;");

    /// <summary>Kontrol genişliği: masaüstünde sabit (default 120px), mobilde tam genişlik.</summary>
    public static string Control(bool isMobile, int width = 120)
        => isMobile ? "width:100%;" : $"width:{width}px;";
}
