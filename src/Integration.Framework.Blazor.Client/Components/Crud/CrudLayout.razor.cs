using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Services;
using Integration.Framework.Blazor.Client.Services.Base;

namespace Integration.Framework.Blazor.Client.Components.Crud
{
    public partial class CrudLayout<TGetDto, TListDto, TViewModel, TKey> : IDisposable
    {
        [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? GridAttributes { get; set; }

        [Parameter] public bool ValidateOnPropertyChange { get; set; } = true;

        [Parameter, EditorRequired] public ICrudStateService<TGetDto, TListDto, TKey, TViewModel> StateService { get; set; } = default!;

        /// <summary>Server-side grid veri kaynağı (CrudPageBase.GridDataSource). Verilirse DxGrid server-mode'a geçer.</summary>
        [Parameter] public object? DataSource { get; set; }

        /// <summary>Arama kutusu metni değişince sayfaya bildirir (server-side filtre).</summary>
        [Parameter] public EventCallback<string> OnSearchChanged { get; set; }
        [Parameter] public EventCallback OnNewClick { get; set; }
        [Parameter] public EventCallback<TListDto> OnUpdateClick { get; set; }
        [Parameter] public EventCallback OnDeleteClick { get; set; }
        [Parameter] public EventCallback OnRefreshClick { get; set; }
        
        [Parameter] public EventCallback OnSaveClick { get; set; }
        [Parameter] public EventCallback OnSaveAndNewClick { get; set; }
        
        [Parameter] public RenderFragment? GridColumns { get; set; }
        [Parameter] public IEnumerable<GridColumnDefinition>? Columns { get; set; }
        [Parameter] public RenderFragment<TViewModel>? EditPageContent { get; set; }

        [Parameter] public string? PageTitle { get; set; }
        [Parameter] public string? EntityName { get; set; }

        /// <summary>Toolbar'a sayfaya özel ek aksiyonlar (ör. "Şubeler" drill action'ı).</summary>
        [Parameter] public RenderFragment? ToolbarActions { get; set; }

        IGrid Grid { get; set; } = default!;
        string? SearchText { get; set; }

        // Mobil arama ikonuyla açılıp kapanan DxGrid gömülü arama kutusu.
        private bool _showGridSearch;
        private void ToggleGridSearch() => _showGridSearch = !_showGridSearch;

        // IsActive filtre switch durumu. İkili: true = Aktif kayıtlar, false = Pasif kayıtlar.
        private bool? _activeFilter;

        // TListDto IIsActive ise IsActive filtresi geçerlidir (yoksa whitelist'te IsActive olmaz → hata).
        private static readonly bool IsActiveFilterable =
            typeof(Integration.Framework.Base.Dtos.Interfaces.IIsActive).IsAssignableFrom(typeof(TListDto));

        protected override void OnInitialized()
        {
            StateService.OnReloadRequested += ReloadGrid;

            // Varsayılan: yalnız IIsActive grid'lerde ilk yükleme "Aktif" kayıtları gösterir
            // (switch varsayılan ON ile tutarlı). İlk fetch'ten ÖNCE set edildiği için reload gerekmez.
            if (IsActiveFilterable)
            {
                _activeFilter = true;
                if (DataSource is GridListDataSource<TListDto> source)
                    source.ActiveFilter = true;
            }
        }

        // Sayfa RequestReload çağırınca grid sunucudan taze sayfayı çeker.
        private void ReloadGrid() => InvokeAsync(() =>
        {
            Grid?.Reload();
            return Task.CompletedTask;
        });

        // Switch değişince aktif server-side veri kaynağına filtreyi uygula ve grid'i yeniden çek.
        private Task OnActiveFilterChanged(bool? value)
        {
            _activeFilter = value;
            if (DataSource is GridListDataSource<TListDto> source)
            {
                source.ActiveFilter = value;
                StateService.RequestReload();   // grid'i sunucudan yeniden çeker (search ile aynı mekanizma)
            }
            return Task.CompletedTask;
        }

        private async Task OnToolbarSearch(string text)
        {
            SearchText = text;
            await OnSearchChanged.InvokeAsync(text);
        }

        public void Dispose()
        {
            if (StateService != null)
            {
                StateService.OnReloadRequested -= ReloadGrid;
            }
        }

        // -- Row Events --
        private async Task OnRowClick(GridRowClickEventArgs e)
        {
            if (!StateService.IsGrantedUpdate)
            {
                return;
            }
            var item = (TListDto)Grid.GetDataItem(e.VisibleIndex);
            if (item != null)
            {
                await OnUpdateClick.InvokeAsync(item);
            }
        }

        // -- Export Logic --
        // Export'a özel ağır DevExpress assembly'leri (Pdf/Printing/Drawing) boot'tan çıkarıldı
        // (csproj BlazorWebAssemblyLazyLoad). İlk export tıklamasında burada yüklenir; sonraki
        // tıklamalarda runtime cache'inden gelir (idempotent). Açılış payload'ı ~10MB daha küçük.
        [Inject] private LazyAssemblyLoader LazyAssemblyLoader { get; set; } = default!;

        private static readonly string[] ExportAssemblies =
        {
            "DevExpress.Printing.v25.2.Core.wasm",
            "DevExpress.Pdf.v25.2.Core.wasm",
            "DevExpress.Pdf.v25.2.Drawing.wasm",
            "DevExpress.Drawing.v25.2.wasm",
        };

        private bool _exportAssembliesLoaded;

        private async Task EnsureExportAssembliesAsync()
        {
            if (_exportAssembliesLoaded) return;
            await LazyAssemblyLoader.LoadAssembliesAsync(ExportAssemblies);
            _exportAssembliesLoaded = true;
        }

        private async Task ExportToExcel()
        {
            await EnsureExportAssembliesAsync();
            await Grid.ExportToXlsxSafeAsync("Export");
        }

        private async Task PrintGrid()
        {
            await EnsureExportAssembliesAsync();
            await Grid.ExportToPdfSafeAsync("Export");
        }
    }
}
