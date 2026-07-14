using System;
using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Agnostik GÖRSEL paneli — bir emtia kartının görsel drill'i (URL/blob; ilk=varsayılan=ana). Good hariç tüm
/// emtia kartları PAYLAŞIR (DRY); sahip AppService agnostik EntityImage (EntityName bağlamı) ile save/load bağlar.
/// Yardımcılar (önizleme/sıra/tekil-varsayılan/dup guard) EntityVariantsPanel'in varyant-görsel drill'iyle aynı desen.</summary>
public partial class EntityImagesPanel
{
    [Parameter, EditorRequired] public List<EntityImageEditDto> Images { get; set; } = default!;

    /// <summary>Drill başlık ikonu — sahip emtianın entity ikonu (override; boş = jenerik ürün ikonu).</summary>
    [Parameter] public string? EntityIcon { get; set; }

    /// <summary>Maksimum görsel adedi (null = sınırsız).</summary>
    [Parameter] public int? MaxItems { get; set; }

    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private DrillList<EntityImageEditDto>? _imageDrill;

    private static string? PreviewSrcOf(EntityImageEditDto image)
    {
        return image.SourceType == ProductImageSourceType.Url ? image.Url : image.PreviewDataUrl;
    }

    private int NextImageOrder()
    {
        return Images.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    // Tekil-varsayılan: kaydedilen görsel varsayılansa diğerlerinin bayrağı düşer (sunucu ReplaceFor "ilki kalır" ezmesin).
    private void TransferDefaultImage(EntityImageEditDto saved)
    {
        if (!saved.IsDefault)
        {
            return;
        }

        foreach (var other in Images.Where(x => x.ClientKey != saved.ClientKey && x.IsDefault))
        {
            other.IsDefault = false;
        }
    }

    // Aynı karta aynı URL/dosya adı iki kez girilemez (case-duyarsız; sunucu ReplaceFor'da da dedup).
    private string? ImageSaveGuard(EntityImageEditDto candidate)
    {
        var others = Images.Where(x => x.ClientKey != candidate.ClientKey).ToList();
        var url = candidate.Url?.Trim();
        var duplicateUrl = url is { Length: > 0 }
            && others.Any(x => string.Equals(x.Url?.Trim(), url, StringComparison.OrdinalIgnoreCase));
        var duplicateFile = candidate.FileName is { Length: > 0 }
            && others.Any(x => string.Equals(x.FileName, candidate.FileName, StringComparison.OrdinalIgnoreCase));
        return duplicateUrl || duplicateFile ? L["TradeXpress:Image:ImageDuplicate"].Value : null;
    }
}
