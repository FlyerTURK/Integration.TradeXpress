namespace Integration.Framework.Blazor.Client.Components.Crud
{
    /// <summary>
    /// Grid arama modu (liste + drill ortak).
    /// <list type="bullet">
    /// <item><see cref="ServerSide"/>: arama metni sunucuya gider → TÜM kayıtlarda arar (GridListDataSource → reload).</item>
    /// <item><see cref="InGrid"/>: arama metni grid'in YÜKLÜ verisi üzerinde istemci filtresi (DxGrid.SearchText).</item>
    /// </list>
    /// Varsayılanlar: persistent liste sayfaları = <see cref="ServerSide"/> (istenirse InGrid); in-memory DrillList =
    /// <see cref="InGrid"/> (ileride persistent drill için <see cref="ServerSide"/>'a açılabilir).
    /// </summary>
    public enum GridSearchMode
    {
        ServerSide,
        InGrid,
    }
}
