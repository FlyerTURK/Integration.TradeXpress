using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.Framework.Blazor.Client.Components.Shared;

/// <summary>Sayısal ön-tanımlı (preset) combo — hazır değerler dropdown'da seçilir YA DA AllowUserInput ile
/// liste-dışı serbest sayı yazılır (ör. KDV %: 1/10/20 + custom). decimal Value @bind eder; DevExpress
/// AllowUserInput'ta serbest değer yalnız Text'e düştüğünden Text tek kaynaktır (parse → Value).</summary>
public partial class NumericPresetComboBox
{
    [Parameter] public decimal Value { get; set; }
    [Parameter] public EventCallback<decimal> ValueChanged { get; set; }
    [Parameter] public Expression<Func<decimal>>? ValueExpression { get; set; }

    /// <summary>Dropdown'da gösterilecek hazır değerler (ör. KDV: 1, 10, 20). Çağıran verir.</summary>
    [Parameter] public IEnumerable<decimal> Presets { get; set; } = Array.Empty<decimal>();

    /// <summary>Gösterim formatı (ör. "N1"). null → tam sayıysa ondalıksız, değilse sade kültür formatı.</summary>
    [Parameter] public string? DisplayFormat { get; set; }

    [Parameter] public bool Enabled { get; set; } = true;
    [Parameter] public string? NullText { get; set; }

    [CascadingParameter] private EditContext? EditContext { get; set; }

    private string _text = string.Empty;
    private decimal _lastValue;
    private bool _synced;

    protected override void OnParametersSet()
    {
        // Value dışarıdan değişince (ya da ilk kez) metni senkronla; kendi yazımımız (_lastValue) tekrar
        // metne çevrilmez → imleç sıçraması / gereksiz reformat olmaz.
        if (!_synced || Value != _lastValue)
        {
            _synced = true;
            _lastValue = Value;
            _text = Format(Value);
        }
    }

    private async Task OnTextChangedAsync(string text)
    {
        _text = text;
        if (!TryParse(text, out var parsed) || parsed == Value)
        {
            return;
        }

        _lastValue = parsed;
        Value = parsed;
        await ValueChanged.InvokeAsync(parsed);
        if (EditContext is not null && ValueExpression is not null)
        {
            EditContext.NotifyFieldChanged(FieldIdentifier.Create(ValueExpression));
        }
    }

    // DisplayFormat verilmişse onu uygula (ör. "N1" → "10,0"); yoksa tam sayıysa ondalıksız, değilse sade.
    private string Format(decimal value)
    {
        if (!string.IsNullOrEmpty(DisplayFormat))
        {
            return value.ToString(DisplayFormat, CultureInfo.CurrentCulture);
        }

        return value == Math.Truncate(value)
            ? ((long)value).ToString(CultureInfo.CurrentCulture)
            : value.ToString(CultureInfo.CurrentCulture);
    }

    // Boş → 0. Önce kullanıcı kültürü, sonra invariant (nokta/virgül farkına dayanıklı).
    private static bool TryParse(string? text, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0m;
            return true;
        }

        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value)
            || decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}
