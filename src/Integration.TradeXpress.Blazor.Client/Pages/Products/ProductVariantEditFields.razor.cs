using System;
using System.Collections.Generic;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Ürün varyantı drill edit alanları (Code/Name/Description/Status + satılabilir veri: fiyat/para/stok)
/// — graf düğümüne bind. TEK yüzey (Product drill'i); çağıran DxFormLayout'u sağlar.</summary>
public partial class ProductVariantEditFields
{
    [Parameter, EditorRequired] public ProductVariantGraphDto Model { get; set; } = default!;

    /// <summary>Para birimi seçici beslemesi (güncel fiyat listesi; Id = CurrencyUnitId). Host yükler, layout geçirir.</summary>
    [Parameter] public IReadOnlyList<CurrentPriceDto> Units { get; set; } = Array.Empty<CurrentPriceDto>();

    // DrillList cascade ettiği EditContext — LookupComboBox ValueExpression sağlamaz; para birimi değişince dirty ELLE bildirilir.
    [CascadingParameter] private EditContext? EditContext { get; set; }

    /// <summary>Satış fiyatı para birimi değişti — modele yaz + EditContext'e dirty bildir (ValueExpression'sız combo).</summary>
    private void OnSalePriceCurrencyChanged(Guid? currencyUnitId)
    {
        Model.SalePriceCurrencyUnitId = currencyUnitId;
        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.SalePriceCurrencyUnitId)));
    }
}
