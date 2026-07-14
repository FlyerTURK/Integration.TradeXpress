using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.SpecialCodes;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SpecialCodes;

/// <summary>Özel Kod picker — bir (EntityName, PropertyName) bağlamının kodlarını combo'da sunar; seçilen kodun
/// <see cref="SpecialCodeListDto.Code"/>'unu (string) @bind-Value ile dışarı verir. Ekle/Düzelt bağlam ön-dolu
/// popup ile (ViewOpener → SpecialCodeEditHost). LookupComboBox'ın merkezî ✎/+ + tazeleme mekanizması yeniden
/// kullanılır (yalnız add/edit override'ıyla bağlam enjekte edilir).</summary>
public partial class SpecialCodePicker
{
    [Parameter, EditorRequired] public string EntityName { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string PropertyName { get; set; } = string.Empty;

    [Parameter] public string? Value { get; set; }
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }
    [Parameter] public Expression<Func<string?>>? ValueExpression { get; set; }

    [Parameter] public bool Enabled { get; set; } = true;
    [Parameter] public bool ReadOnly { get; set; }
    [Parameter] public string? NullText { get; set; }

    /// <summary>Değer boşken (yalnız BİR kez) listedeki İLK kodu otomatik seç. Yeni kayıtlarda "varsayılan ilk değer"
    /// için — mevcut kayıtları kirletmemek adına çağıran <c>IsNew</c> ile gate'ler (ör. Good stok birimi).</summary>
    [Parameter] public bool SelectFirstWhenEmpty { get; set; }

    [Inject] private IViewOpener ViewOpener { get; set; } = default!;
    [Inject] private IPopupService PopupService { get; set; } = default!;

    private List<SpecialCodeListDto> _data = new();
    private string? _loadedEntityName;
    private string? _loadedPropertyName;
    private bool _appliedFirstDefault;

    protected override async Task OnParametersSetAsync()
    {
        // Bağlam ilk kez ya da değişince yükle (aynı bağlamda tekrar yükleme yok).
        if (EntityName != _loadedEntityName || PropertyName != _loadedPropertyName)
        {
            _loadedEntityName = EntityName;
            _loadedPropertyName = PropertyName;
            await ReloadAsync();
        }
    }

    private async Task ReloadAsync()
    {
        _data = await SpecialCodeAppService.GetForContextAsync(EntityName, PropertyName);

        // Varsayılan ilk değer — yalnız bir kez, değer boşken (kullanıcı sonra değiştirebilir/temizleyebilir).
        if (SelectFirstWhenEmpty && !_appliedFirstDefault && string.IsNullOrEmpty(Value) && _data.Count > 0)
        {
            _appliedFirstDefault = true;
            await ValueChanged.InvokeAsync(_data[0].Code);
        }

        await InvokeAsync(StateHasChanged);
    }

    // + → yeni kod popup'ı (bağlam ön-dolu). Auto-select yapılmaz (Value=Code, EntityChange Id taşır) — kaydedilince
    // liste tazelenir, kullanıcı seçer. Reload'u return default ile tetiklemeyiz; EntityChange zaten OnLookupReload'u sürer.
    private async Task<string?> OnAddRequestedAsync()
    {
        await OpenEditAsync(null);
        return default;
    }

    // ✎ → seçili kodun (Value) satırını bul → o Id ile düzenleme popup'ı (bağlam ön-dolu).
    private async Task OnEditRequestedAsync(string code)
    {
        var row = _data.FirstOrDefault(x => x.Code == code);
        if (row is null)
        {
            return;
        }

        await OpenEditAsync(row.Id);
    }

    private Task OpenEditAsync(Guid? id)
    {
        var extra = new Dictionary<string, object>
        {
            { nameof(SpecialCodeEditHost.EntityName), EntityName },
            { nameof(SpecialCodeEditHost.PropertyName), PropertyName },
            { "OnClosed", EventCallback.Factory.Create(this, () => PopupService.Close()) },
        };
        object? editId = id is { } g ? g : null;
        return ViewOpener.OpenAsync(typeof(SpecialCodeEditHost), editId, string.Empty, null, extra);
    }
}
