using Microsoft.AspNetCore.Components.WebAssembly.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Framework.Blazor.Client.Components.Crud
{
    /// <summary>
    /// <see cref="IGridExportAssemblyLoader"/> uygulaması. Export assembly'leri WASM'da boot'tan çıkarıldı
    /// (csproj BlazorWebAssemblyLazyLoad; açılış ~10MB küçük); ilk export'ta burada lazy-load edilir, sonraki
    /// çağrılar idempotent. Blazor Server'da <c>OperatingSystem.IsBrowser()</c>=false ve LazyAssemblyLoader
    /// kayıtlı olmadığından no-op'tur (assembly'ler host sürecinde zaten var). <see cref="LazyAssemblyLoader"/>
    /// IServiceProvider üzerinden OPSİYONEL çözülür → Server DI'da yoksa patlamaz. Scoped (per-circuit).
    /// </summary>
    public sealed class GridExportAssemblyLoader : IGridExportAssemblyLoader
    {
        private static readonly string[] ExportAssemblies =
        {
            "DevExpress.Printing.v25.2.Core.wasm",
            "DevExpress.Pdf.v25.2.Core.wasm",
            "DevExpress.Pdf.v25.2.Drawing.wasm",
            "DevExpress.Drawing.v25.2.wasm",
        };

        private readonly IServiceProvider _serviceProvider;
        private bool _loaded;

        public GridExportAssemblyLoader(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task EnsureLoadedAsync()
        {
            if (_loaded)
            {
                return;
            }

            var lazyLoader = _serviceProvider.GetService<LazyAssemblyLoader>();
            if (!OperatingSystem.IsBrowser() || lazyLoader == null)
            {
                _loaded = true;   // Server (veya loader yok) → assembly'ler zaten yüklü kabul edilir.
                return;
            }

            await lazyLoader.LoadAssembliesAsync(ExportAssemblies);
            _loaded = true;
        }
    }
}
