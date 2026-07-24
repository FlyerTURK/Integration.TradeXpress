using System;
using System.Collections.Generic;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Companies;

/// <summary>Branch DUMB layout code-behind — Model bağlama + HQ kilidi yakalama. Alanlar (adres editörü dahil) +
/// kasalar paylaşılan bileşenlerde (BranchEditFields / VaultDrill; DRY).</summary>
public partial class BranchLayout : CrudComponentBase
{
    [Parameter, EditorRequired] public BranchGetDto Model { get; set; } = default!;
    [Parameter] public List<CurrencyUnitListDto> Units { get; set; } = new();
    [Parameter] public bool IsNew { get; set; }

    // Drill (Kasalar) değişimini forma bildir — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    // Kök branch HQ switch: form içi devir yok → yüklü (orijinal) HQ ise kilitli. İlk parametre set'inde yakala.
    private bool _originalWasHq;
    private bool _hqCaptured;

    protected override void OnParametersSet()
    {
        if (!_hqCaptured && Model is not null)
        {
            _originalWasHq = Model.IsHeadquarters;
            _hqCaptured = true;
        }
    }
}
