using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
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

    /// <summary>true → chrome INLINE değil, <c>DxPopup</c> içinde açılır (gradyan başlık HeaderContentTemplate'te, kimlik
    /// korunur; Kaydet/Geri footer'da). X/Escape = Geri (draft iptal). Default false (mevcut inline tüketiciler değişmez).</summary>
    [Parameter] public bool Popup { get; set; }

    /// <summary>Popup görünürlüğü (Popup=true iken). Türev IsPanelOpen'a bağlar; false → popup kapanır.</summary>
    [Parameter] public bool Visible { get; set; }

    /// <summary>Popup görünürlüğü değişince (X/Escape) türeve bildirir — false gelince ayrıca <see cref="OnBack"/> (iptal) tetiklenir.</summary>
    [Parameter] public EventCallback<bool> VisibleChanged { get; set; }

    /// <summary>Popup genişliği — reçete satırı geniş (yatay alan dizisi) → varsayılan geniş, içerik yatay kayar.</summary>
    [Parameter] public string Width { get; set; } = "min(96vw, 1100px)";

    /// <summary>Popup ÜST TOOLBAR'ında Kaydet'ten ÖNCE render edilen opsiyonel içerik (ör. reçete:
    /// "Tüm varyantlara uygula" checkbox'ı). Yalnız popup modda.</summary>
    [Parameter] public RenderFragment? FooterLeading { get; set; }

    /// <summary>Popup başlığı ikonu (standart header icon+caption — diğer edit formları gibi). Boşsa yalnız caption.</summary>
    [Parameter] public string? Icon { get; set; }

    /// <summary>Standart <c>EditToolbar</c> için edit controller. Verilirse popup ÜST toolbar'ı STANDART EditToolbar
    /// olur (edit formlarıyla AYNI); boşsa basit Kaydet/Geri butonları.</summary>
    [Parameter] public ISplitEditActions? EditController { get; set; }

    /// <summary>Standart popup başlığı (EditHeaderView): ikon + caption (=Title, BÜYÜK harf — süreç paneli şerit paritesi).</summary>
    private TabHeaderData BuildHeader()
    {
        return new TabHeaderData { FormCaption = (Title ?? string.Empty).ToUpper(), IconCssClass = Icon };
    }

    // Popup X/Escape/kapanış → türeve bildir; kapanışta (false) Geri (draft iptal) — inline "Geri" ile aynı sonuç.
    private async Task OnPopupVisibleChanged(bool visible)
    {
        await VisibleChanged.InvokeAsync(visible);
        if (!visible)
        {
            await OnBack.InvokeAsync();
        }
    }

    // Süreç paneli şerit stili (ProcessPanelBase.StripStyle + şerit div'inin inline stilleri) — birebir görünüm.
    private string HeaderStripStyle()
    {
        return "height:34px; border-radius:4px 4px 0 0; background:" + HeaderGradient + "; "
             + "display:flex; align-items:center; padding:0 10px; color:#fff; font-size:0.85rem; "
             + "font-weight:700; letter-spacing:0.06em; text-transform:uppercase; white-space:nowrap; "
             + "overflow:hidden; text-overflow:ellipsis;";
    }
}
