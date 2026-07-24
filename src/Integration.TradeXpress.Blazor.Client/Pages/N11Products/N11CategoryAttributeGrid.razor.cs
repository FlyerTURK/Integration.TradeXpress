using System;
using System.Collections.Generic;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Products;

/// <summary>N11 kategori attribute'larının hücre-içi düzenlenen grid'i — zorunlu ve opsiyonel (GPSR) satır kümeleri
/// için ORTAK markup (DRY; üst form iki kez tüketir). Kaydetme mantığı ÜST formda kalır (<see cref="RowSaving"/>
/// delege): üst, düzenlenen satırı <c>_attributeRows</c> içinde AttributeId ile bulup uygular — bu bileşen yalnız
/// görüntü + edit template sağlar (durum tutmaz).</summary>
public partial class N11CategoryAttributeGrid : CrudComponentBase
{
    public N11CategoryAttributeGrid()
    {
        LocalizationResource = typeof(TradeXpressResource);
    }

    /// <summary>Grid'e bağlanan satırlar (üst formun _attributeRows'undan süzülmüş zorunlu ya da opsiyonel alt-küme).</summary>
    [Parameter, EditorRequired] public IReadOnlyList<N11AttributeCellRow> Rows { get; set; } = Array.Empty<N11AttributeCellRow>();

    /// <summary>Hücre kaydı üst forma delege edilir (üst, satırı AttributeId ile bulup değeri uygular).</summary>
    [Parameter] public EventCallback<GridEditModelSavingEventArgs> RowSaving { get; set; }
}
