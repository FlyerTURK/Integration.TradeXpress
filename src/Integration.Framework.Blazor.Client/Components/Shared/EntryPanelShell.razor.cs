using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Shared;

/// <summary>EntryPanelShell code-behind — başlık şeridi stili + parametreler (markup .razor'da; @code yasak).</summary>
public partial class EntryPanelShell
{
    /// <summary>Şerit başlığı (tip adı vb.; CSS uppercase'ler).</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Şerit arka planı — default KIRMIZI gradyan (süreç paneli çıkış rengi). Gerekirse override.</summary>
    [Parameter] public string HeaderGradient { get; set; } = "var(--gradient-red)";

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public EventCallback OnSave { get; set; }
    [Parameter] public EventCallback OnBack { get; set; }

    /// <summary>Kaydet butonu aktifliği (çift-gönderim/geçersiz-durum koruması türevde).</summary>
    [Parameter] public bool SaveEnabled { get; set; } = true;

    // Süreç paneli şerit stili (ProcessPanelBase.StripStyle + şerit div'inin inline stilleri) — birebir görünüm.
    private string HeaderStripStyle()
    {
        return "height:34px; border-radius:4px 4px 0 0; background:" + HeaderGradient + "; "
             + "display:flex; align-items:center; padding:0 10px; color:#fff; font-size:0.85rem; "
             + "font-weight:700; letter-spacing:0.06em; text-transform:uppercase; white-space:nowrap; "
             + "overflow:hidden; text-overflow:ellipsis;";
    }
}
