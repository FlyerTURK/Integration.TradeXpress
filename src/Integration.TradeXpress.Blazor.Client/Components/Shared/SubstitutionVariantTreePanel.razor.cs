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

    /// <summary>ÜRÜN modu: verilirse panel bu DÜZ listeyi düzenler (<c>Product.SubstitutionOverrideVariantIds</c>;
    /// yerinde mutasyon — Items'ın grup modundaki sözleşmesiyle simetrik). Kapsam gruptan BİR KEZ içe aktarılır
    /// (<see cref="ImportGroupScopeOnce"/>); sonrasında liste tek doğrudur ve BOŞ kalması "hiçbirini istemiyorum"
    /// demektir — gruba geri düşülmez (2026-07-27 kararı).</summary>
    [Parameter] public List<Guid>? OverrideVariantIds { get; set; }

    /// <summary>Küme değişince fırlatılır — layout EditChanged cascade'ine bağlar; dirty'yi form JSON-snapshot
    /// kıyasıyla kendisi hesaplar (geri alınan seçim formu temiz bırakır — PermissionPanel'in snapshot amacı).</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    /// <summary>Maden kataloğu — tek parça ağırlığı (<c>StableQuantity</c>) buradan okunur.</summary>
    [Parameter] public IReadOnlyList<MetalListDto> Metals { get; set; } = Array.Empty<MetalListDto>();

    /// <summary>Muadilin hedef miktarı (gram). Verilmişse, tek parçası bile hedefi AŞAN madenler listede
    /// GÖSTERİLMEZ: 8 gramlık muadilde 10/20/50/100 gramlık külçe hiçbir kombinasyona giremez, ekranda
    /// durması yalnız listeyi şişirir (2026-07-27 Hakan kararı).</summary>
    [Parameter] public decimal? TargetQuantity { get; set; }

    private readonly List<MetalNode> _nodes = new();

    /// <summary>Gruptan içe aktarma bu bileşen ömründe yapıldı mı — bir kez. Aksi hâlde kullanıcı hepsini
    /// kaldırdığında sonraki render aynı kümeyi geri yazar ve kaldırma imkânsız hâle gelir.</summary>
    private bool _scopeImported;

    /// <summary>OVERRIDE modunda mı — ürün formu düz listeyi düzenler, grup kalemleri referans kalır.</summary>
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
        ImportGroupScopeOnce();
    }

    /// <summary>
    /// GRUPTAN İÇE AKTARMA — ürün bir gruba bağlandığında grubun kapsamı ürünün KENDİ listesine bir kez
    /// kopyalanır. Sonrasında grubun bu üründe işi kalmaz: kullanıcı bir varyantın işaretini kaldırdığında
    /// karar ürüne aittir, sessizce gruba geri dönülmez (2026-07-27 Hakan kararı — öncesinde boş liste
    /// "gruptan devral" demekti ve kaldırma eylemi etkisiz kalıyordu).
    /// <para>Yalnız liste HİÇ doldurulmamışken çalışır; kullanıcı sonradan hepsini kaldırırsa liste boş kalır
    /// ve bu bilinçli "hiçbirini istemiyorum" kararıdır — tekrar doldurulmaz (<see cref="_scopeImported"/>).</para>
    /// </summary>
    private void ImportGroupScopeOnce()
    {
        if (!IsOverrideMode || _scopeImported || OverrideVariantIds is not { } scope || scope.Count > 0)
        {
            return;
        }

        _scopeImported = true;

        foreach (var node in _nodes)
        {
            scope.AddRange(InheritedSet(node));
        }

        if (scope.Count > 0)
        {
            _ = OnChanged.InvokeAsync();
        }
    }

    // Düz kalem listesi → 2 seviyeli ağaç.
    private void BuildNodes()
    {
        _nodes.Clear();

        var variantsByMetal = Variants
            .Where(v => v.VariantId != null)
            .GroupBy(v => v.CommodityId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var weightByMetal = Metals.ToDictionary(m => m.Id, m => m.StableQuantity);

        foreach (var item in Items.Where(i => !i.IsDeleted && i.MetalId != null))
        {
            var metalVariants = variantsByMetal.GetValueOrDefault(item.MetalId!.Value) ?? new List<MetalVariantLookupDto>();
            if (metalVariants.Count == 0)
            {
                continue; // varyantı olmayan maden — seçilecek kapsam yok
            }

            // Tek parçası bile hedefi aşan maden hiçbir kombinasyona giremez → listede yer kaplamasın.
            // Ağırlık bilinmiyorsa (katalogda yok) ELEME YAPILMAZ — görünmeyen kayıt, sessiz veri kaybıdır.
            if (TargetQuantity is { } target && target > 0m
                && weightByMetal.TryGetValue(item.MetalId!.Value, out var pieceWeight)
                && pieceWeight > target)
            {
                continue;
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
    /// Ürün modunda yalnız İÇE AKTARMA kaynağıdır (bir kez kopyalanır; sonrasında referans değildir).</summary>
    /// <summary>
    /// Madenin GRUPTAN devralınan kapsamı. Kalem kendi listesini doldurmuşsa o; DOLDURMAMIŞSA madenin TÜM
    /// varyantları.
    ///
    /// <para><b>Neden hepsi (2026-07-28 Hakan):</b> önceden yalnız ana varyant devralınıyordu ve kullanıcı
    /// muadilde kullanmak istediği her varyantı tek tek işaretlemek zorunda kalıyordu. Doğru varsayılan
    /// tersidir: hepsi aday, kullanıcı istemediğini ÇIKARIR (8 gramlık muadilde 8 gram üstü emtiaları
    /// kaldırmak gibi) — daraltma seyrek, tek tek eklemek her seferinde.</para>
    ///
    /// <para>Varyantı olmayan (legacy) madende ana varyant tek adaydır; liste boş kalmasın.</para>
    /// </summary>
    private HashSet<Guid> InheritedSet(MetalNode node)
    {
        if (node.Item.IncludedVariantIds.Count > 0)
        {
            return node.Item.IncludedVariantIds.ToHashSet();
        }

        var all = node.Variants.Where(v => v.VariantId != null).Select(v => v.VariantId!.Value).ToHashSet();
        if (all.Count > 0)
        {
            return all;
        }

        return node.MainVariantId is { } mainId ? new HashSet<Guid> { mainId } : new HashSet<Guid>();
    }

    /// <summary>Madenin ürün kapsamı — düz listenin bu madenin varyantlarıyla kesişimi (boş = bu maden istenmiyor).</summary>
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

    /// <summary>Tümünü seç — TEK YÖNLÜ (buton). Daraltmayı kullanıcı tek tek yapar; toplu "hiçbirini seçme"
    /// diye bir durum yok, çünkü kapsam boş kalamaz (bkz. <see cref="WouldLeaveScopeEmpty"/>).</summary>
    /// <summary>Tümünü seç — DIŞARIDAN çağrılabilir (grup başlığındaki buton). Bkz. <see cref="HasUnselected"/>.</summary>
    public Task SelectAllAsync()
    {
        return OnSelectAllClickedAsync();
    }

    /// <summary>Seçilmemiş varyant KALDI MI — başlıktaki buton yalnız yapacak iş varken gösterilir.</summary>
    public bool HasUnselected
    {
        get { return !IsEverythingSelected(); }
    }

    private async Task OnSelectAllClickedAsync()
    {
        // TOPLU yaz + TEK bildirim. Düğüm başına OnMetalToggledAsync çağırmak her adımda OnChanged
        // tetikliyordu → parent render → OnParametersSet → BuildNodes → _nodes.Clear() — döngü hâlâ o
        // listenin üzerindeyken ("Collection was modified" çöküşü, 2026-07-28 Hakan). Yazma doğrudan
        // Store* ile yapılır (seçme yönünde kapsam-boşalma guard'ı zaten anlamsız), bildirim en sonda
        // bir kez gider — N gereksiz render da ortadan kalkar.
        foreach (var node in _nodes)
        {
            var selected = node.Variants
                .Where(v => v.VariantId != null)
                .Select(v => v.VariantId!.Value)
                .ToHashSet();

            if (IsOverrideMode)
            {
                StoreOverride(node, selected);
            }
            else
            {
                StoreGroupItem(node, selected);
            }
        }

        if (OnChanged.HasDelegate)
        {
            await OnChanged.InvokeAsync();
        }
    }

    /// <summary>Panelin TAMAMINDA seçili varyant sayısı.</summary>
    private int TotalSelectedCount()
    {
        return _nodes.Sum(n => SelectionSet(n).Count);
    }

    /// <summary>Her madenin HER varyantı seçili mi — "Tümünü Seç" butonu yalnız yapacak bir iş varken görünür.</summary>
    private bool IsEverythingSelected()
    {
        return _nodes.Count > 0 && _nodes.All(n => SelectionSet(n).Count == n.Variants.Count);
    }

    /// <summary>
    /// Bu kaldırma işlemi kapsamı TAMAMEN boşaltır mı? Muadillikte hiçbir emtia seçili olmayan bir kapsam
    /// anlamsızdır — çözücünün elinde aday kalmaz, ürün hiç üretilemez. Bu yüzden son seçim korunur:
    /// işlem sessizce uygulanmaz (uyarı verilmez — kullanıcı zaten daraltma yapıyor, engel görünür olur;
    /// 2026-07-27 Hakan kararı). Daraltma meşrudur: 8 gramlık muadilde 8 gram üstü emtiaları kaldırmak gibi.
    /// </summary>
    private bool WouldLeaveScopeEmpty(int removedCount)
    {
        return TotalSelectedCount() - removedCount <= 0;
    }

    // Maden düğümü: işaret → TÜM varyantlar dahil; kaldır → statükoya dön (boş = yalnız ana varyant).
    private async Task OnMetalToggledAsync(MetalNode node, bool? value)
    {
        // Bu madeni tamamen kaldırmak kapsamı boşaltıyorsa işlem UYGULANMAZ (sessiz engel — bkz. WouldLeaveScopeEmpty).
        if (value != true && WouldLeaveScopeEmpty(SelectionSet(node).Count))
        {
            return;
        }

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

        // Son kalan seçimi kaldırmaya çalışmak kapsamı boşaltır → sessizce uygulanmaz.
        if (!included && WouldLeaveScopeEmpty(1))
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

    /// <summary>GRUP modu: seçimi kalem DTO'suna AYNEN yazar (yeni semantik 2026-07-24: varsayılan TÜM varyantlar,
    /// maden eklenince OnItemSaved materyalize eder → "boş=ana" normalizasyonu KALKTI). Kullanıcı ne bıraktıysa o
    /// whitelist'tir; hepsini çıkarırsa boş liste → resolver ana varyanta düşer (emniyet). Liste sırası lookup
    /// sırasından üretilir (deterministik JSON → form dirty kıyası kararlı).</summary>
    private void StoreGroupItem(MetalNode node, HashSet<Guid> selected)
    {
        node.Item.IncludedVariantIds = node.Variants
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
