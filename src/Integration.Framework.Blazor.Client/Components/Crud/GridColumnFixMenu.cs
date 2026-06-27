using System.Threading.Tasks;
using DevExpress.Blazor;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Grid HEADER context-menü'süne "Sola Sabitle / Sağa Sabitle / Sabitlemeyi Kaldır" (kolonu sabitle/çöz)
/// öğelerini ekler — SEÇİM kolonu HARİÇ. TxGrid (tüm standart grid'ler) + CrudLayout (list page'ler) ortak
/// kullanır → tek kaynak, her grid'de aynı. (Metinler şu an TR sabit; gerekirse parametreye çevrilip lokalize edilir.)
/// </summary>
public static class GridColumnFixMenu
{
    public static void Add(GridCustomizeContextMenuEventArgs args)
    {
        if (args.Context is not GridHeaderCommandContext header) return;
        if (header.Column is IGridSelectionColumn) return;   // seçim kolonu sabitleme menüsüne DAHİL DEĞİL

        Task Apply(GridColumnFixedPosition pos)
        {
            header.Grid.BeginUpdate();
            header.Column.FixedPosition = pos;
            header.Grid.EndUpdate();
            return Task.CompletedTask;
        }

        var left = args.Items.AddCustomItem("Sola Sabitle", () => Apply(GridColumnFixedPosition.Left));
        left.BeginGroup = true;   // üstüne ayraç (yerleşik öğelerden ayrılsın)
        args.Items.AddCustomItem("Sağa Sabitle", () => Apply(GridColumnFixedPosition.Right));
        args.Items.AddCustomItem("Sabitlemeyi Kaldır", () => Apply(GridColumnFixedPosition.None));
    }
}
