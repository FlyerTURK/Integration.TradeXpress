using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.SpecialCodes;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SpecialCodes;

/// <summary>Özel Kod edit formu içeriği (dumb Layout) — GetDto'ya bind eder. Parent combo aynı bağlamdaki kodlardan,
/// kendini hariç tutar (döngü önlemenin ilk savunması; sunucu da guard'lar).</summary>
public partial class SpecialCodeLayout
{
    [Parameter, EditorRequired] public SpecialCodeGetDto Model { get; set; } = default!;

    /// <summary>Aynı bağlamdaki özel kodlar (parent adayları) — host besler.</summary>
    [Parameter] public IReadOnlyList<SpecialCodeListDto> ParentOptions { get; set; } = Array.Empty<SpecialCodeListDto>();

    /// <summary>Parent seçenekleri — düzenlenen kaydın kendisi hariç (kendini parent yapamaz).</summary>
    private IEnumerable<SpecialCodeListDto> ParentCandidates
        => ParentOptions.Where(x => x.Id != Model.Id);
}
