using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Substitutions;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>
/// Muadil grubu "Varyant Kapsamı" ağacı — 2 seviye: maden düğümleri (grubun kalemleri) → varyant çocukları
/// (opt-in checkbox). PermissionEditorPanel deseninin uyarlaması (tri-state üst düğüm + girintili çocuklar);
/// seviye sayısı SABİT 2 olduğundan recursive RenderFragment gerekmez (markup'ta iç içe foreach).
/// <para><b>İş gerekçesi:</b> metal varyantlarının işçilik/maliyeti ayrışıyor (yeni tarihli çeyrek toptancıdan
/// işçilikli; eski tarihli perakendeden işçiliksiz hurda) → hangi varyantın muadile katılacağı satır bazında
/// SEÇİLEBİLİR olmalı. Yeni doğan varyant OTOMATİK dahil değildir (opt-in).</para>
/// <para><b>Persist yolu:</b> panel <see cref="SubstitutionGroupItemGraphDto.IncludedVariantIds"/>'ı in-memory
/// düzenler; kayıt GRUP kaydıyla olur (ayrı servis commit'i YOK — kalem grafı zaten grup input'unda taşınır).
/// BOŞ liste = yalnız ANA varyant (statüko); "{yalnız ana}" seçimi boş listeye normalize edilir (tek temsil).</para>
/// </summary>
public partial class SubstitutionVariantTreePanel : CrudComponentBase
{
    public SubstitutionVariantTreePanel()
    {
        LocalizationResource = typeof(TradeXpressResource);
    }

    /// <summary>Grubun in-memory kalem grafı (Model.Items) — GRUP modunda panel DTO'ları DOĞRUDAN düzenler;
    /// OVERRIDE modunda (bkz. <see cref="OverrideVariantIds"/>) kalemler SALT-OKUNUR referanstır
    /// (devralınan küme buradan çizilir, IncludedVariantIds'a YAZILMAZ).</summary>
    [Parameter, EditorRequired] public List<SubstitutionGroupItemGraphDto> Items { get; set; } = new();

    /// <summary>Maden→varyant lookup satırları (MetalAppService.GetVariantLookupAsync; IsMain ana varyantı işaretler).</summary>
    [Parameter, EditorRequired] public IReadOnlyList<MetalVariantLookupDto> Variants { get; set; } = Array.Empty<MetalVariantLookupDto>();

    /// <summary>OVERRIDE modu (Dilim-3, ürün yüzeyi): verilirse panel bu DÜZ listeyi düzenler
    /// (<c>Product.SubstitutionOverrideVariantIds</c>; yerinde mutasyon — Items'ın grup modundaki sözleşmesiyle
    /// simetrik). <b>Maden başına boş kesişim = GRUPTAN DEVRAL</b> (resolver: override ?? included ?? ana) —
    /// devralınan küme pasif/gri "(devralınan)" etiketiyle referans görünür. Grup modundaki "{yalnız ana} → boş"
    /// normalizasyonu BURADA YOKTUR (boş=devral semantiği farklı: yalnız-ana override'ı meşru bir daraltmadır).</summary>
    [Parameter] public List<Guid>? OverrideVariantIds { get; set; }

    /// <summary>Küme değişince fırlatılır — layout EditChanged cascade'ine bağlar; dirty'yi form JSON-snapshot
    /// kıyasıyla kendisi hesaplar (geri alınan seçim formu temiz bırakır — PermissionPanel'in snapshot amacı).</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    private readonly List<MetalNode> _nodes = new();

    /// <summary>OVERRIDE modunda mı — ürün yüzeyi düz listeyi düzenler, grup kalemleri referans kalır.</summary>
    private bool IsOverrideMode
    {
        get { return OverrideVariantIds is not null; }
    }

    /// <summary>Ağaç düğümü: kalem DTO'su + o madenin varyant satırları (lookup sırası korunur → deterministik JSON).</summary>
    private sealed class MetalNode
    {
        public required SubstitutionGroupItemGraphDto Item { get; init; }
        public required string MetalCode { get; init; }
        public required List<MetalVariantLookupDto> Variants { get; init; }
        public Guid? MainVariantId { get; init; }
    }

    protected override void OnParametersSet()
    {
        // Kalem drill'i aynı liste referansını yerinde değiştirir (ekle/sil/metal değişimi) → her parent
        // render'ında ağaç yeniden kurulur (listeler küçük; PermissionPanel.BuildTree ucuzluğunda).
        BuildNodes();
    }

    // Düz kalem listesi → 2 seviyeli ağaç.
    private void BuildNodes()
    {
        _nodes.Clear();

        var variantsByMetal = Variants
            .Where(v => v.VariantId != null)
            .GroupBy(v => v.CommodityId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var item in Items.Where(i => !i.IsDeleted && i.MetalId != null))
        {
            var metalVariants = variantsByMetal.GetValueOrDefault(item.MetalId!.Value) ?? new List<MetalVariantLookupDto>();
            if (metalVariants.Count == 0)
            {
                continue; // varyantı olmayan maden — seçilecek kapsam yok
            }

            _nodes.Add(new MetalNode
            {
                Item          = item,
                MetalCode     = ResolveMetalCode(item, metalVariants),
                Variants      = metalVariants,
                MainVariantId = metalVariants.FirstOrDefault(v => v.IsMain)?.VariantId,
            });
        }
    }

    // Drill yeni satırında MetalCode henüz boş olabilir (OnItemSaved doldurur) → lookup'tan tamamla.
    private static string ResolveMetalCode(SubstitutionGroupItemGraphDto item, List<MetalVariantLookupDto> metalVariants)
    {
        return string.IsNullOrEmpty(item.MetalCode) ? metalVariants[0].MetalCode : item.MetalCode;
    }

    /// <summary>GRUBUN etkin dahil kümesi — BOŞ liste = yalnız ANA varyant değişmezinin ağaçtaki karşılığı.
    /// Override modunda DEVRALINAN kümedir (pasif referans görünümü).</summary>
    private HashSet<Guid> InheritedSet(MetalNode node)
    {
        if (node.Item.IncludedVariantIds.Count > 0)
        {
            return node.Item.IncludedVariantIds.ToHashSet();
        }

        return node.MainVariantId is { } mainId ? new HashSet<Guid> { mainId } : new HashSet<Guid>();
    }

    /// <summary>Madenin OVERRIDE seçimi — düz listenin bu madenin varyantlarıyla kesişimi (boş = devral).</summary>
    private HashSet<Guid> OverrideSet(MetalNode node)
    {
        if (OverrideVariantIds is not { } overrides)
        {
            return new HashSet<Guid>();
        }

        var metalVariantIds = node.Variants
            .Where(v => v.VariantId != null)
            .Select(v => v.VariantId!.Value)
            .ToHashSet();
        return overrides.Where(metalVariantIds.Contains).ToHashSet();
    }

    /// <summary>Checkbox'ların bağlandığı SEÇİM kümesi — grup modunda etkin dahil küme; override modunda
    /// yalnız override üyeliği (devralınan küme işaretli DEĞİL, gri etiketle referans gösterilir).</summary>
    private HashSet<Guid> SelectionSet(MetalNode node)
    {
        return IsOverrideMode ? OverrideSet(node) : InheritedSet(node);
    }

    private bool IsIncluded(MetalNode node, MetalVariantLookupDto variant)
    {
        return variant.VariantId is { } id && SelectionSet(node).Contains(id);
    }

    private int IncludedCount(MetalNode node)
    {
        return SelectionSet(node).Count;
    }

    /// <summary>Override modunda bu maden gruptan mı devralıyor (override kesişimi boş)?</summary>
    private bool IsInheriting(MetalNode node)
    {
        return IsOverrideMode && OverrideSet(node).Count == 0;
    }

    /// <summary>Devralınan kümede mi — override modunda devral durumundaki gri "(devralınan)" etiketi için.</summary>
    private bool IsInheritedMember(MetalNode node, MetalVariantLookupDto variant)
    {
        return variant.VariantId is { } id && InheritedSet(node).Contains(id);
    }

    // Tri-state maden durumu (PermissionEditorPanel.GroupState deseni): hepsi/hiç/kısmi.
    private bool? MetalState(MetalNode node)
    {
        var count = IncludedCount(node);
        if (count == 0)
        {
            return false;
        }

        return count == node.Variants.Count ? true : (bool?)null;
    }

    // Maden düğümü: işaret → TÜM varyantlar dahil; kaldır → statükoya dön (boş = yalnız ana varyant).
    private async Task OnMetalToggledAsync(MetalNode node, bool? value)
    {
        var selected = value == true
            ? node.Variants.Where(v => v.VariantId != null).Select(v => v.VariantId!.Value).ToHashSet()
            : new HashSet<Guid>();

        await StoreAsync(node, selected);
    }

    private async Task OnVariantToggledAsync(MetalNode node, MetalVariantLookupDto variant, bool included)
    {
        if (variant.VariantId is not { } id)
        {
            return;
        }

        var selected = SelectionSet(node);
        if (included)
        {
            selected.Add(id);
        }
        else
        {
            selected.Remove(id);
        }

        await StoreAsync(node, selected);
    }

    /// <summary>Seçimi moda göre yazar — grup modu kalem DTO'suna, override modu düz ürün listesine.</summary>
    private async Task StoreAsync(MetalNode node, HashSet<Guid> selected)
    {
        if (IsOverrideMode)
        {
            StoreOverride(node, selected);
        }
        else
        {
            StoreGroupItem(node, selected);
        }

        if (OnChanged.HasDelegate)
        {
            await OnChanged.InvokeAsync();
        }
    }

    /// <summary>GRUP modu: seçimi kalem DTO'suna yazar — normalizasyon (boş=ana değişmezinin TEK temsili):
    /// boş küme → boş liste ("hiçbiri" temsil edilmez, ana varyant her zaman dahildir) ve "{yalnız ana}" → boş
    /// liste. Liste sırası lookup sırasından üretilir (deterministik JSON → form dirty kıyası kararlı).
    /// Sunucu aynı normalizasyonu yazma sınırında da zorlar (client güven sınırı değildir).</summary>
    private void StoreGroupItem(MetalNode node, HashSet<Guid> selected)
    {
        var onlyMain = node.MainVariantId is { } mainId && selected.Count == 1 && selected.Contains(mainId);
        node.Item.IncludedVariantIds = selected.Count == 0 || onlyMain
            ? new List<Guid>()
            : node.Variants
                .Where(v => v.VariantId is { } id && selected.Contains(id))
                .Select(v => v.VariantId!.Value)
                .ToList();
    }

    /// <summary>OVERRIDE modu: düz listeyi TÜM düğümlerin güncel seçiminden yeniden kurar (düğüm sırası +
    /// lookup sırası → deterministik JSON, form dirty kıyası kararlı; yerinde mutasyon — Model referansı korunur).
    /// "{yalnız ana} → boş" normalizasyonu YOK (boş=devral; yalnız-ana override'ı meşru daraltmadır). Mevcut
    /// düğümlere ait olmayan bayat id'ler ilk düzenlemede kendiliğinden düşer (öz-onarım).</summary>
    private void StoreOverride(MetalNode node, HashSet<Guid> selected)
    {
        if (OverrideVariantIds is not { } overrides)
        {
            return;
        }

        var rebuilt = new List<Guid>();
        foreach (var current in _nodes)
        {
            var set = ReferenceEquals(current, node) ? selected : OverrideSet(current);
            rebuilt.AddRange(current.Variants
                .Where(v => v.VariantId is { } id && set.Contains(id))
                .Select(v => v.VariantId!.Value));
        }

        overrides.Clear();
        overrides.AddRange(rebuilt.Distinct());
    }
}
