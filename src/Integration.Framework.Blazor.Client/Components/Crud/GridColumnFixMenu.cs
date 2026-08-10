using System;
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
    /// <param name="onResetLayout">"Görünümü Sıfırla" — verilirse menüye eklenir. Kaydedilmiş kolon düzenini
    /// (genişlik/sıra/sıralama/sabitleme) siler ve grid'i varsayılana döndürür.
    /// <para><b>Neden gerekli:</b> düzen kullanıcı başına KALICI (StateKey). Kolon eklenip çıkarıldığında ya da
    /// kullanıcı düzeni bozduğunda, eski kayıt yeni tasarımı sessizce eziyor ve geri dönüşün tek yolu
    /// veritabanına elle dokunmak oluyordu — 2026-08-10'da kanal ürünleri grid'inde tam bu yaşandı.</para></param>
    public static void Add(GridCustomizeContextMenuEventArgs args, Func<Task>? onResetLayout = null)
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

        if (onResetLayout is not null)
        {
            var reset = args.Items.AddCustomItem("Görünümü Sıfırla", onResetLayout);
            reset.BeginGroup = true;   // sabitleme öğelerinden ayrı: bu kolonu değil TÜM grid'i etkiler
        }
    }
}
