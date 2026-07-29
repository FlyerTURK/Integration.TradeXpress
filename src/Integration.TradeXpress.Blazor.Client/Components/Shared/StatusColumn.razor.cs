using System;
using Integration.Framework.Base.Dtos;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>"Status" (IsActive) grid kolonu — parametreler + hücre içi toggle.</summary>
public partial class StatusColumn
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
}
