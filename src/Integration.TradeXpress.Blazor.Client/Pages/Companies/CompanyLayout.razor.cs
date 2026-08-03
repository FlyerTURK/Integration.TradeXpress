using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Companies;

/// <summary>Company DUMB Layout — parametreler + yeni-kayıt merkez şube çözümü.</summary>
public partial class CompanyLayout
{
    [Parameter, EditorRequired] public CompanyGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }
    [Parameter] public List<CurrencyUnitListDto> Units { get; set; } = new();
    [Parameter] public List<CountryListDto> Countries { get; set; } = new();

    // Quick-add/edit (host bağlar → +/✎ butonları) — CompanyEditFields combo'larına forward.
    [Parameter] public Func<Task<Guid>>? OnAddCurrency { get; set; }
    [Parameter] public Func<Guid, Task>? OnEditCurrency { get; set; }
    [Parameter] public Func<Task<Guid?>>? OnAddCountry { get; set; }
    [Parameter] public Func<Guid?, Task>? OnEditCountry { get; set; }

    /// <summary>Şube adresindeki coğrafya combo'ları yeni il/ilçe eklenince tazelensin (host bağlar).</summary>
    [Parameter] public Func<Task>? OnReferenceDataReload { get; set; }

    // Şube/Kasa drill değişimini forma bildir (dirty/toolbar/*) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    // Şirketin kendi HQ switch'i: form içi devir yok → yüklü (orijinal) HQ ise kilitli. İlk parametre set'inde yakala.
    private bool _originalWasHq;
    private bool _hqCaptured;

    /// <summary>YENİ şirkette gömülü gösterilecek merkez şube — <c>CompanyEditHost.ApplyNewCompanyDefaults</c>
    /// tarafından tek eleman olarak kurulur. Silinmiş satır elenir; hiç yoksa null (gömülü grup çizilmez).</summary>
    private BranchGraphDto? FirstBranch
    {
        get { return Model.Branches.FirstOrDefault(b => !b.IsDeleted); }
    }

    protected override void OnParametersSet()
    {
        if (!_hqCaptured && Model is not null)
        {
            _originalWasHq = Model.IsHeadquarters;
            _hqCaptured = true;
        }
    }
}
