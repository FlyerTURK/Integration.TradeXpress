using Integration.TradeXpress.Accounts;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Accounts;

/// <summary>SubAccount cari edit formunun ortak alanları (Code/Name/Description/Status) — tek kaynak bileşen.
/// Model arayüze bağlıdır → hem <c>SubAccountGetDto</c> (popup) hem <c>SubAccountGraphDto</c> (drill) aynı
/// bileşeni besler; form her ikisinde de AYNI görünür.</summary>
public partial class SubAccountEditFields
{
    [Parameter, EditorRequired] public ISubAccountEditableFields Model { get; set; } = default!;
}
