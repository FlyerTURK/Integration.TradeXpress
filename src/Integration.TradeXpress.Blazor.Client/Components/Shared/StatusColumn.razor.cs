using System;
using System.Threading.Tasks;
using Integration.Framework.Base.Dtos;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>"Status" (IsActive) grid kolonu — parametreler + hücre içi toggle.</summary>
public partial class StatusColumn : IHandleEvent
{
    /// <summary>Aktiflik alanı (varsayılan IsActive).</summary>
    [Parameter] public string FieldName { get; set; } = "IsActive";

    /// <summary>Başlık (varsayılan localize "Status").</summary>
    [Parameter] public string? Caption { get; set; }

    [Parameter] public string Width { get; set; } = "110px";

    /// <summary>Açık kolon sırası (negatif = markup sırası; GridLinkColumn ile aynı kural).</summary>
    [Parameter] public int VisibleIndex { get; set; } = -1;

    [Parameter] public bool Visible { get; set; } = true;

    /// <summary>Hücreden doğrudan aktif/pasif yapılabilsin mi — <b>VARSAYILAN AÇIK</b> (2026-07-27 Hakan).
    /// Satır DTO'su <see cref="IHasIsActive"/> uygulamıyorsa kolon kendiliğinden salt-okunur kalır.
    /// Salt-okunur listelerde açıkça <c>false</c> geçilir.</summary>
    [Parameter] public bool Editable { get; set; } = true;

    /// <summary>Form "kirli" bildirimi — DrillList/EditForm zincirinden CASCADE gelir, kullanım yerlerinde
    /// ayrıca bağlanması gerekmez. Kalıcılaşma ana formun Kaydet'inde olur.</summary>
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    /// <summary>
    /// Grid içi hızlı toggle: DTO'yu YERİNDE günceller (in-memory graf deseni) ve formu kirli işaretler.
    /// Kayıt ana formun Kaydet'inde olur — burada sunucuya gidilmez.
    /// <para><b>SENKRON olmak zorunda:</b> bu bileşen bir DevExpress kolon (nested settings) şablonunda
    /// yaşıyor ve orada asenkron render yasak — async bir işleyici "Async rendering is not allowed here"
    /// ile çöker (2026-07-27'de bu yüzden düştü).</para>
    /// </summary>
    private void Toggle(IHasIsActive target, bool value)
    {
        target.IsActive = value;
        EditChanged?.Invoke();
    }

    /// <summary>
    /// Olay sonrası OTOMATİK re-render'ı bastırır (Blazor'un resmî IHandleEvent deseni).
    /// <para><b>Neden:</b> bu bileşen bir DevExpress kolon (nested settings) wrapper'ı; 25.2.8'de
    /// SettingsRenderer, SENKRON handler'ın ardından gelen otomatik StateHasChanged'i bile "Async rendering
    /// is not allowed here" ile fırlatıyor (25.2.5 tolere ediyordu — hücre toggle'ı 2026-08-01'de bu yüzden
    /// çöktü). Bileşenin kendi re-render'ına zaten İHTİYAÇ YOK: DxCheckBox iç modelini kendisi günceller,
    /// form tazelemesi EditChanged cascade'i ile üst zincirden gelir.</para>
    /// </summary>
    Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, object? arg)
    {
        return callback.InvokeAsync(arg);
    }
}
