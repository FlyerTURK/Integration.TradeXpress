using System.Threading.Tasks;
using DevExpress.Blazor;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// DevExpress <see cref="IGrid"/> için null-güvenli sayfalama ve dışa aktarma yardımcıları.
/// Grid referansı (@ref) henüz atanmamışsa tüm çağrılar güvenle no-op olur.
/// </summary>
public static class GridExtensions
{
    public static bool CanGoToPreviousPage(this IGrid? grid)
        => grid != null && grid.PageIndex > 0;

    public static bool CanGoToNextPage(this IGrid? grid)
        => grid != null && grid.PageIndex < grid.GetPageCount() - 1;

    public static void GoToPreviousPage(this IGrid? grid)
    {
        if (grid.CanGoToPreviousPage())
        {
            grid!.PageIndex--;
        }
    }

    public static void GoToNextPage(this IGrid? grid)
    {
        if (grid.CanGoToNextPage())
        {
            grid!.PageIndex++;
        }
    }

    public static Task ExportToXlsxSafeAsync(this IGrid? grid, string fileName)
        => grid?.ExportToXlsxAsync(fileName) ?? Task.CompletedTask;

    public static Task ExportToPdfSafeAsync(this IGrid? grid, string fileName)
        => grid?.ExportToPdfAsync(fileName) ?? Task.CompletedTask;
}
