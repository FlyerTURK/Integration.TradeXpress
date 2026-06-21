using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Shared;

/// <summary>Yapısal başlık (L1/L2/L3 + ikon + dirty) görseli — code-behind. Markup: EditHeaderView.razor,
/// stiller: EditHeaderView.razor.css. <see cref="TabHeaderData"/> Framework GlobalUsings'ten gelir.</summary>
public partial class EditHeaderView
{
    /// <summary>Çizilecek yapısal başlık. null ise hiçbir şey render edilmez.</summary>
    [Parameter] public TabHeaderData? Header { get; set; }

    /// <summary>Tab strip için küçük varyant (daha sıkı font/gap).</summary>
    [Parameter] public bool Compact { get; set; }

    /// <summary>Dirty "*" işareti gösterilsin mi (tab/popup: true; top-panel: false).</summary>
    [Parameter] public bool ShowDirty { get; set; }

    /// <summary>Kaydedilmemiş değişiklik var mı (tek kaynak: MdiTab.IsDirty / edit IsDirty). ShowDirty ile birlikte "*".</summary>
    [Parameter] public bool IsDirty { get; set; }

    /// <summary>Dirty "*" için ekran-okuyucu etiketi (a11y); tüketici lokalize geçer. Boşsa yalnız görsel "*".</summary>
    [Parameter] public string? DirtyLabel { get; set; }
}
