using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Substitutions;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Substitutions;

public partial class SubstitutionGroupLayout
{
    public SubstitutionGroupLayout()
    {
        LocalizationResource = typeof(TradeXpressResource);
    }

    [Parameter, EditorRequired] public SubstitutionGroupGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    /// <summary>Maden picker adayları (host'tan) — yalnız adet-hesaplı + standart gramajlı madenler.</summary>
    [Parameter] public IReadOnlyList<MetalListDto> Metals { get; set; } = Array.Empty<MetalListDto>();

    /// <summary>Maden→varyant lookup satırları (host'tan; GetVariantLookupAsync) — Varyant Kapsamı ağacına iner.</summary>
    [Parameter] public IReadOnlyList<MetalVariantLookupDto> MetalVariants { get; set; } = Array.Empty<MetalVariantLookupDto>();

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i (Account deseni).
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private DrillList<SubstitutionGroupItemGraphDto>? _itemsDrill;

    // Yeni satır listenin SONUNA eklenir (en düşük tüketim önceliği) — mevcut sıra korunur.
    private SubstitutionGroupItemGraphDto NewItem()
    {
        var nextOrder = Model.Items
            .Where(i => !i.IsDeleted)
            .Select(i => i.DisplayOrder)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        return new SubstitutionGroupItemGraphDto { DisplayOrder = nextOrder };
    }

    // Drill popup'ı kaydedince grid'de görünen maden kodunu seçimden tazele (MetalCode display-only) +
    // muadil varyant kapsamını MATERYALİZE et.
    private Task OnItemSaved(SubstitutionGroupItemGraphDto item)
    {
        item.MetalCode = Metals.FirstOrDefault(m => m.Id == item.MetalId)?.Code ?? string.Empty;

        if (item.MetalId is not { } metalId)
        {
            return Task.CompletedTask;
        }

        var metalVariantIds = MetalVariants
            .Where(v => v.CommodityId == metalId && v.VariantId != null)
            .Select(v => v.VariantId!.Value)
            .ToList();

        // Varyant kapsamı varsayılanı (kullanıcı kararı 2026-07-24 "create anında sabitlenir"): maden eklenince o anki
        // TÜM varyantlar dahil edilir; kullanıcı "Varyant Kapsamı" sekmesinden istemediğini ÇIKARIR ve sonradan doğan
        // varyant bu SABİT listeye otomatik girmez.
        //
        // MATERYALİZASYON YALNIZ İKİ DURUMDA (kod-inceleme düzeltmesi):
        //  (a) satır YENİ (Id boş — henüz kaydedilmemiş) → ilk kez dolduruluyor;
        //  (b) satırın MADENİ DEĞİŞTİRİLMİŞ → eldeki id'ler eski madene ait, bu maden için geçersiz (hesapta
        //      "IncludedVariantNotFound" fail-fast'ine yol açardı) → bu madenin kapsamıyla yenilenir.
        // MEVCUT satırın BOŞ listesine ASLA dokunulmaz: boş = "yalnız ana varyant" — sunucunun (NormalizeIncludedVariants)
        // kasıtlı daraltmayı normalize ettiği temsil. Burayı koşulsuz doldurmak, DrillList güncelleme yolunda da
        // çalıştığı için, kullanıcının bilinçli daraltmasını alakasız bir alan düzenlemesinde sessizce geri alıyordu.
        var isNewRow = item.Id == Guid.Empty;
        var matchesCurrentMetal = item.IncludedVariantIds.Any(metalVariantIds.Contains);
        if ((isNewRow && item.IncludedVariantIds.Count == 0)
            || (item.IncludedVariantIds.Count > 0 && !matchesCurrentMetal))
        {
            item.IncludedVariantIds = metalVariantIds;
        }

        return Task.CompletedTask;
    }
}
