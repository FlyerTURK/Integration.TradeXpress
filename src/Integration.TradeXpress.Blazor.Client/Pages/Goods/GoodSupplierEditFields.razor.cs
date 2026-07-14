using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Goods;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Goods;

/// <summary>Mamül tedarikçisi (drill satırı) edit alanları — cari (Account→SubAccount cascade) + temin
/// koşulları (fiyat/para birimi/vergi dahil/temin süresi). Nullable combo ⇄ non-nullable DTO id adaptörleri
/// burada; Account değişince SubAccount temizlenir (bayat alt-hesap kalmasın).</summary>
public partial class GoodSupplierEditFields
{
    [Parameter, EditorRequired] public GoodSupplierDto Model { get; set; } = default!;
    [Parameter] public IReadOnlyList<AccountListDto> Accounts { get; set; } = Array.Empty<AccountListDto>();
    [Parameter] public IReadOnlyList<SubAccountListDto> SubAccounts { get; set; } = Array.Empty<SubAccountListDto>();
    [Parameter] public IReadOnlyList<CurrencyUnitListDto> CurrencyUnits { get; set; } = Array.Empty<CurrencyUnitListDto>();

    // Lookup ekle/düzelt sonrası host listeyi tazeler (EditComponentType + EntityChange → OnLookupReloadRequested).
    [Parameter] public EventCallback OnReloadAccounts { get; set; }
    [Parameter] public EventCallback OnReloadSubAccounts { get; set; }
    [Parameter] public EventCallback OnReloadCurrencyUnits { get; set; }

    // Seçili hesaba ait alt hesaplar (cascade).
    private IEnumerable<SubAccountListDto> FilteredSubAccounts
    {
        get
        {
            return Model.AccountId == Guid.Empty
                ? Enumerable.Empty<SubAccountListDto>()
                : SubAccounts.Where(s => s.AccountId == Model.AccountId);
        }
    }

    // Combo Guid? ⇄ DTO Guid (Empty ≡ seçilmemiş) adaptörü — cari hesap ZORUNLU (Guid, non-null).
    private Guid? SelectedAccountId
    {
        get { return Model.AccountId == Guid.Empty ? null : Model.AccountId; }
    }

    private void OnAccountChanged(Guid? value)
    {
        var next = value ?? Guid.Empty;
        if (next == Model.AccountId)
        {
            return;
        }

        // Cari hesap değişti → alt hesap TEMİZLENİR (otomatik seçim YOK; kullanıcı isterse alt hesabı seçer).
        Model.AccountId = next;
        Model.SubAccountId = null;
    }
}
