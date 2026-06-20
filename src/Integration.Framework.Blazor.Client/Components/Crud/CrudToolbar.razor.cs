using Microsoft.AspNetCore.Components;
using Integration.Framework.Blazor.Client.Services.Base;

namespace Integration.Framework.Blazor.Client.Components.Crud
{
    public partial class CrudToolbar<TGetDto, TListDto, TKey>
    {
        [Parameter, EditorRequired] public ICrudStateService<TListDto, TKey> StateService { get; set; } = default!;

        [Parameter] public string? SearchText { get; set; }
        [Parameter] public EventCallback<string> SearchTextChanged { get; set; }

        [Parameter] public EventCallback OnNewClick { get; set; }
        [Parameter] public EventCallback OnDeleteClick { get; set; }
        [Parameter] public EventCallback OnRefreshClick { get; set; }
        
        [Parameter] public EventCallback OnExportToExcelClick { get; set; }
        [Parameter] public EventCallback OnPrintPdfClick { get; set; }

        /// <summary>Sayfaya özel ek toolbar aksiyonları (ör. "Şubeler" drill action'ı).</summary>
        [Parameter] public RenderFragment? CustomActions { get; set; }

        [Parameter] public bool? ActiveFilter { get; set; }
        [Parameter] public EventCallback<bool?> ActiveFilterChanged { get; set; }

        // TListDto IIsActive implement ediyorsa IsActive filtre switch'i gösterilir.
        private static readonly bool ShowActiveFilter =
            typeof(Integration.Framework.Base.Dtos.Interfaces.IIsActive).IsAssignableFrom(typeof(TListDto));

        // İkili: Switch ON => Aktif kayıtlar (true); OFF => Pasif kayıtlar (false). "Tümü" yok.
        // Varsayılan ON (ActiveFilter null ya da true → ON; yalnız false → OFF).
        private bool ActiveSwitchValue => ActiveFilter != false;
        private Task OnActiveSwitchChanged(bool on) => ActiveFilterChanged.InvokeAsync((bool?)on);

        /// <summary>MainLayout'tan cascade edilen mobil bilgisi. Mobilde arama kutusu (textbox) gizlenir;
        /// yerine sade arama ikonu görünür ve tıklanınca DxGrid'in gömülü arama kutusu açılır.</summary>
        [CascadingParameter(Name = "IsMobile")] public bool IsMobile { get; set; }

        /// <summary>Mobil arama ikonuna basınca tetiklenir — CrudLayout DxGrid'in gömülü aramasını açar.</summary>
        [Parameter] public EventCallback OnToggleGridSearch { get; set; }

        private string? _localSearchText;

        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            if (_localSearchText != SearchText)
            {
                _localSearchText = SearchText;
            }
        }

        private void OnLocalTextChanged(string newText)
        {
            _localSearchText = newText;
            SearchTextChanged.InvokeAsync(_localSearchText);
        }

        private Task OnSearchButtonClick()
        {
            return SearchTextChanged.InvokeAsync(_localSearchText);
        }
    }
}
