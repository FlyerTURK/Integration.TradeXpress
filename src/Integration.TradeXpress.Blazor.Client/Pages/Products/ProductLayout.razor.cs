using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Product dumb layout code-behind — Model bağlama + varyant drill referansı + dirty cascade.</summary>
public partial class ProductLayout
{
    [Parameter, EditorRequired] public ProductGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    // Reçete drill'inin katalog lookup verisi — host yükler (DUMB layout servis çağırmaz).
    [Parameter] public IReadOnlyList<MetalListDto> Metals { get; set; } = Array.Empty<MetalListDto>();
    [Parameter] public IReadOnlyList<MetalVariantLookupDto> MetalVariants { get; set; } = Array.Empty<MetalVariantLookupDto>();
    [Parameter] public IReadOnlyList<ScrapListDto> Scraps { get; set; } = Array.Empty<ScrapListDto>();
    [Parameter] public IReadOnlyList<FutureListDto> Futures { get; set; } = Array.Empty<FutureListDto>();
    [Parameter] public IReadOnlyList<JewelryListDto> Jewelries { get; set; } = Array.Empty<JewelryListDto>();
    [Parameter] public IReadOnlyList<StoneListDto> Stones { get; set; } = Array.Empty<StoneListDto>();
    [Parameter] public IReadOnlyList<ServiceListDto> Services { get; set; } = Array.Empty<ServiceListDto>();
    [Parameter] public IReadOnlyList<CurrentPriceDto> Units { get; set; } = Array.Empty<CurrentPriceDto>();

    /// <summary>Varsayılan para birimi lookup verisi — host yükler (DUMB layout servis çağırmaz).</summary>
    [Parameter] public IReadOnlyList<CurrencyUnitListDto> CurrencyUnits { get; set; } = Array.Empty<CurrencyUnitListDto>();

    /// <summary>Inline döviz ekle/düzelt sonrası lookup listesini host tazeler (EntityChange tetikler).</summary>
    [Parameter] public EventCallback OnReloadCurrencyUnits { get; set; }

    // Nitelik + varyant drill'leri artık JENERİK paylaşılan panellerde (EntityAttributesPanel / EntityVariantsPanel);
    // yalnız görsel drill'i bu layout'ta kalır.
    private DrillList<ProductImageGraphDto>? _imageDrill;

    /// <summary>Görsel önizleme kaynağı — URL tipli doğrudan URL, yüklenmişte sunucunun doldurduğu data-URL.</summary>
    private static string? PreviewSrcOf(ProductImageGraphDto image)
    {
        return image.SourceType == ProductImageSourceType.Url ? image.Url : image.PreviewDataUrl;
    }

    // Cancel geri alabilsin diye kopya üzerinde düzenleme (upload'ın blob yazımı geri alınmaz — süpürücü işi;
    // ama Model.Images'taki CANLI satır iptalde mutate edilmemiş kalır).
    private static ProductImageGraphDto CloneImage(ProductImageGraphDto source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<ProductImageGraphDto>(json)!;
    }

    /// <summary>Tekil-bayrak transferi (HQ-devri deseni): kaydedilen görsel VARSAYILAN işaretliyse diğerlerinin
    /// bayrağı düşer — aksi halde sunucu EnsureSingleDefault "ilki kalır" kuralıyla kullanıcının YENİ seçimini
    /// sessizce geri alırdı (review bulgusu).</summary>
    private void TransferDefaultImage(ProductImageGraphDto saved)
    {
        if (!saved.IsDefault)
        {
            return;
        }

        foreach (var other in Model.Images.Where(x => x.ClientKey != saved.ClientKey && x.IsDefault))
        {
            other.IsDefault = false;
        }
    }

    /// <summary>Görsel kaydetme engeli: aynı ürüne aynı URL ya da aynı dosya adı İKİ KEZ girilemez
    /// (2026-07-07 kullanıcı kararı; case-duyarsız). Sunucu SetImages'ta da aynı kural (savunma).</summary>
    private string? ImageSaveGuard(ProductImageGraphDto candidate)
    {
        var others = Model.Images.Where(x => x.ClientKey != candidate.ClientKey);
        var url = candidate.Url?.Trim();
        var duplicateUrl = url is { Length: > 0 }
            && others.Any(x => string.Equals(x.Url?.Trim(), url, StringComparison.OrdinalIgnoreCase));
        var duplicateFile = candidate.FileName is { Length: > 0 }
            && others.Any(x => string.Equals(x.FileName, candidate.FileName, StringComparison.OrdinalIgnoreCase));
        return duplicateUrl || duplicateFile ? L["TradeXpress:Product:ImageDuplicate"].Value : null;
    }

    /// <summary>Özel bilgi satırı kaydetme engeli — key boşsa satır kabul edilmez (SetSpecialInfo sunucuda da boş key eler).</summary>
    private string? SpecialInfoSaveGuard(ProductSpecialInfoDto item)
    {
        return string.IsNullOrWhiteSpace(item.Key) ? L["Product:SpecialInfoKeyRequired"].Value : null;
    }

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    /// <summary>Reçete değişince CANLI maliyet — host yapar (persistsiz hesap, varyant bazında); tam kayıt gerekmez.</summary>
    [Parameter] public Func<ProductVariantGraphDto, Task>? OnRecipeChanged { get; set; }

    /// <summary>Reçete satırı eklenince/değişince/silinince: önce CANLI maliyet (host), sonra form dirty.</summary>
    private async Task HandleRecipeChangedAsync(ProductVariantGraphDto variant)
    {
        if (OnRecipeChanged is not null)
        {
            await OnRecipeChanged(variant);
        }

        EditChanged?.Invoke();
    }

    /// <summary>Nitelik/değer değişince (EntityAttributesPanel.OnAttributesChanged) host varyantları OTOMATİK yeniden
    /// üretir (VariantGraphMerge — kullanıcı düzenlemeleri korunur). Layout DUMB kalır (servis çağırmaz); işi host yapar.</summary>
    [Parameter] public EventCallback OnGenerateVariants { get; set; }

    // Yeni görsel eklenince Sıra No OTOMATİK artar (max + 1; boşsa 1). Nitelik/değer sırası JENERİK panelde.
    private static int NextOrder(IEnumerable<ProductImageGraphDto> items)
    {
        return items.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }
}
