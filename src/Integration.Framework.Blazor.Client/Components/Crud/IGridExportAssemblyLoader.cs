namespace Integration.Framework.Blazor.Client.Components.Crud
{
    /// <summary>
    /// Export'a özel ağır DevExpress assembly'lerini (Pdf/Printing/Drawing) ilk export'ta yüklemeyi garanti eder.
    /// Paylaşılan tek kaynak: CrudLayout (liste) ve DrillList (drill) aynı yolu kullanır. Blazor Server'da no-op
    /// (assembly'ler zaten süreçte); WASM'da lazy-load (açılış payload'ı küçük kalsın diye boot'tan çıkarıldılar).
    /// </summary>
    public interface IGridExportAssemblyLoader
    {
        Task EnsureLoadedAsync();
    }
}
