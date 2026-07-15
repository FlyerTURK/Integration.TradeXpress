using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Attachments;
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

    /// <summary>Uzantı slot'u — varyant edit formuna entity-özel alanlar ekler (typed; ör. Good fiyat/stok). Boş = yok.</summary>
    [Parameter] public RenderFragment<TVariant>? ExtraFields { get; set; }

    /// <summary>Çekirdek Stok Adedi kolonu + edit alanını göster. Stoğu ledger'dan (VoucherLine) türeten entity'ler
    /// (ör. Good) <c>false</c> geçer — statik stok anlamsız; pazaryeri push'lu entity'ler (Product) varsayılan <c>true</c>.</summary>
    [Parameter] public bool ShowStockQuantity { get; set; } = true;

    /// <summary>Varyant edit popup'ında VARYANT-ÖZEL MEDYA panelini (+ grid poster önizlemesini) göster (yeni DAM; v.Media).
    /// Sahip AppService save/load'ı EntityMediaAppService ReplaceFor/GetFor ile bağlar. Varsayılan kapalı.</summary>
    [Parameter] public bool ShowImages { get; set; }

    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private DrillList<TVariant>? _variantDrill;

    // Varyant grid thumbnail'i — varyantın VARSAYILAN medyasının poster önizlemesi (yoksa ilki; hiç yoksa null). Yalnız ShowImages kolonu.
    private static string? VariantPreviewSrc(TVariant v)
    {
        if (v.Media == null || v.Media.Count == 0)
        {
            return null;
        }

        var pick = v.Media.FirstOrDefault(m => m.IsDefault) ?? v.Media[0];
        return pick.Media?.PosterUrl;
    }
}
