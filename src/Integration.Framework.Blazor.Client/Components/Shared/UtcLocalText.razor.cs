using System;
using System.Globalization;
using Integration.Framework.Blazor.Client.Timing;
using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Shared;

/// <summary>
/// Yeniden-kullanılabilir gösterim bileşeni: UTC bir timestamp'i (<see cref="Value"/>) kullanıcının
/// yerel saatine (<see cref="IDisplayTimeConverter"/>) çevirip <see cref="Format"/> ile formatlar.
/// <para><b>Yalnız timestamp (an) alanları için</b> — CreationTime, LastModificationTime, RateDate gibi.
/// Date-only iş tarihleri (VoucherDate/DueDate/AsOfDate/ProfitResetDate) wall-clock'tur, ÇEVRİLMEZ →
/// bu bileşene verme, doğrudan formatla.</para>
/// </summary>
public partial class UtcLocalText : ComponentBase
{
    [Inject] private IDisplayTimeConverter Converter { get; set; } = default!;

    /// <summary>Çevrilecek UTC timestamp. <c>null</c> → <see cref="EmptyText"/> gösterilir.</summary>
    [Parameter] public DateTime? Value { get; set; }

    /// <summary>.NET tarih-saat format dizesi (varsayılan gün.ay.yıl saat:dakika).</summary>
    [Parameter] public string Format { get; set; } = "dd.MM.yyyy HH:mm";

    /// <summary><see cref="Value"/> null olduğunda gösterilecek metin (varsayılan boş).</summary>
    [Parameter] public string EmptyText { get; set; } = string.Empty;

    // Render'da çizilen hazır metin (UtcLocalText.razor içinde @_display).
    private string _display = string.Empty;

    protected override void OnParametersSet()
    {
        if (Value is null)
        {
            _display = EmptyText;
            return;
        }

        var local = Converter.ToLocal(Value.Value);
        _display = local.ToString(Format, CultureInfo.CurrentCulture);
    }
}
