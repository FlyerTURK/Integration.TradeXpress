using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Metals;

/// <summary>Metal DUMB Layout code-behind — MetalGetDto. FollowingUnit ZORUNLU + Factor(&gt;0) + işçilik +
/// sikke/adet + TEK temsili görsel (paylaşılan SingleImageEditFields; upload IMetalImageAppService'e bağlanır).</summary>
public partial class MetalLayout
{
    [Parameter, EditorRequired] public MetalGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }
    [Parameter] public IReadOnlyList<CurrencyUnitListDto> CurrencyUnits { get; set; } = Array.Empty<CurrencyUnitListDto>();

    /// <summary>"Varyantları Oluştur" — layout DUMB (servis çağırmaz): host yapar (MetalAppService.GenerateVariantsAsync → Model.Variants).</summary>
    [Parameter] public EventCallback OnGenerateVariants { get; set; }

    [Inject] private IMetalImageAppService MetalImageAppService { get; set; } = default!;

    private record LaborTypeItem(MetalLaborType Value, string Label);
    private List<LaborTypeItem> _laborTypes = new();

    protected override void OnInitialized()
    {
        _laborTypes = new()
        {
            new(MetalLaborType.Amount,   L["Enum:MetalLaborType:Amount"].Value),
            new(MetalLaborType.Quantity, L["Enum:MetalLaborType:Quantity"].Value),
        };
    }

    protected override void OnParametersSet()
    {
        // Paylaşılan görsel bileşeni non-null model ister — eski kayıttan/haritalamadan null gelirse boş model kur.
        Model.Image ??= new MetalImageDto();
    }

    /// <summary>Non-null görsel modeli (OnParametersSet garantisi) — razor attribute'unda '!' yazılamıyor (RZ9986).</summary>
    private MetalImageDto ImageModel
    {
        get { return Model.Image!; }
    }

    /// <summary>Paylaşılan çekirdeğin upload delegesi — madenin görsel servisiyle blob'a yazar.</summary>
    private async Task<SingleImageUploadResult> UploadImageAsync(string fileName, byte[] content)
    {
        var result = await MetalImageAppService.UploadAsync(new MetalImageUploadDto
        {
            FileName = fileName,
            Content = content,
        });

        return new SingleImageUploadResult(result.BlobName, result.PreviewDataUrl);
    }
}
