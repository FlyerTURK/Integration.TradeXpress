using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>JENERİK varyant paneli — kartezyenden üretilen varyantlar (ekleme/silme KAPALI; synchronizer üretir).
/// "Varyantları Oluştur" sahibin <see cref="OnGenerate"/>'ini çağırır (DUMB: servisi sahip host yapar). Çekirdek alanlar
/// (Barkod/Stok/Açıklama/Aktif) düzenlenir; entity-özel alanlar <see cref="ExtraFields"/> slot'unda (TYPED: sahip
/// türetilmiş DTO'suyla, ör. GoodVariantGraphDto → fiyat/stok).</summary>
/// <typeparam name="TVariant">Sahip varyant DTO'su — çekirdek <see cref="EntityVariantGraphDto"/> ya da türevi.</typeparam>
public partial class EntityVariantsPanel<TVariant> where TVariant : EntityVariantGraphDto, new()
{
    [Parameter, EditorRequired] public List<TVariant> Variants { get; set; } = default!;

    /// <summary>Nitelikler — "Oluştur" butonu görünürlüğü için (nitelik yoksa üretilecek kombinasyon yok).</summary>
    [Parameter, EditorRequired] public List<EntityAttributeGraphDto> Attributes { get; set; } = default!;

    /// <summary>"Varyantları Oluştur" tıklandı — sahip host sunucudan üretir (GenerateVariants → Variants doldurur).</summary>
    [Parameter] public EventCallback OnGenerate { get; set; }

    /// <summary>Uzantı slot'u — varyant edit formuna entity-özel alanlar ekler (typed; ör. Good fiyat/stok). Boş = yok.</summary>
    [Parameter] public RenderFragment<TVariant>? ExtraFields { get; set; }

    /// <summary>Çekirdek Stok Adedi kolonu + edit alanını göster. Stoğu ledger'dan (VoucherLine) türeten entity'ler
    /// (ör. Good) <c>false</c> geçer — statik stok anlamsız; pazaryeri push'lu entity'ler (Product) varsayılan <c>true</c>.</summary>
    [Parameter] public bool ShowStockQuantity { get; set; } = true;

    /// <summary>Varyant edit popup'ında VARYANT-ÖZEL görsel drill'ini göster (agnostik EntityImage; v.Images).
    /// Sahip AppService save/load'ı ReplaceForAsync/GetForAsync ile bağlar. Varsayılan kapalı.</summary>
    [Parameter] public bool ShowImages { get; set; }

    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private DrillList<TVariant>? _variantDrill;
    private DrillList<EntityImageEditDto>? _imageDrill;

    private async Task GenerateClickedAsync()
    {
        await OnGenerate.InvokeAsync();
        EditChanged?.Invoke();
    }

    // ── Varyant-özel görsel drill'i (ShowImages) yardımcıları — Good Images sekmesi deseniyle aynı ──

    private static string? PreviewSrcOf(EntityImageEditDto image)
    {
        return image.SourceType == ProductImageSourceType.Url ? image.Url : image.PreviewDataUrl;
    }

    // Varyant grid thumbnail'i — varyantın VARSAYILAN görselinin önizlemesi (yoksa ilki; hiç yoksa null). Yalnız ShowImages kolonu.
    private static string? VariantPreviewSrc(TVariant v)
    {
        if (v.Images == null || v.Images.Count == 0)
        {
            return null;
        }

        var pick = v.Images.FirstOrDefault(i => i.IsDefault) ?? v.Images[0];
        return PreviewSrcOf(pick);
    }

    private static int NextImageOrder(List<EntityImageEditDto> images)
    {
        return images.Select(x => x.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
    }

    // Tekil-varsayılan: kaydedilen görsel varsayılansa diğerlerinin bayrağı düşer (sunucu ReplaceFor "ilki kalır" ezmesin).
    private static void TransferDefaultImage(List<EntityImageEditDto> images, EntityImageEditDto saved)
    {
        if (!saved.IsDefault)
        {
            return;
        }

        foreach (var other in images.Where(x => x.ClientKey != saved.ClientKey && x.IsDefault))
        {
            other.IsDefault = false;
        }
    }

    // Aynı varyanta aynı URL/dosya adı iki kez girilemez (case-duyarsız; sunucu ReplaceFor'da da dedup).
    private string? ImageSaveGuard(List<EntityImageEditDto> images, EntityImageEditDto candidate)
    {
        var others = images.Where(x => x.ClientKey != candidate.ClientKey).ToList();
        var url = candidate.Url?.Trim();
        var duplicateUrl = url is { Length: > 0 }
            && others.Any(x => string.Equals(x.Url?.Trim(), url, StringComparison.OrdinalIgnoreCase));
        var duplicateFile = candidate.FileName is { Length: > 0 }
            && others.Any(x => string.Equals(x.FileName, candidate.FileName, StringComparison.OrdinalIgnoreCase));
        return duplicateUrl || duplicateFile ? L["TradeXpress:Image:ImageDuplicate"].Value : null;
    }
}
