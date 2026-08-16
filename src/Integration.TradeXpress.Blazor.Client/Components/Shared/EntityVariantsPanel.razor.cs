using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Blazor.Client;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>JENERİK varyant paneli — kartezyenden üretilen varyantlar (ekleme/silme KAPALI; synchronizer üretir).
/// "Varyantları Oluştur" sahibin <see cref="OnGenerate"/>'ini çağırır (DUMB: servisi sahip host yapar). Çekirdek alanlar
/// (Barkod/Stok/Açıklama/Aktif) düzenlenir; entity-özel alanlar <see cref="ExtraFields"/> slot'unda (TYPED: sahip
/// türetilmiş DTO'suyla, ör. GoodVariantGraphDto → fiyat/stok).</summary>
/// <typeparam name="TVariant">Sahip varyant DTO'su — çekirdek <see cref="EntityVariantGraphDto"/> ya da türevi.</typeparam>
public partial class EntityVariantsPanel<TVariant> : IDisposable where TVariant : EntityVariantGraphDto, new()
{
    [Parameter, EditorRequired] public List<TVariant> Variants { get; set; } = default!;

    /// <summary>Nitelikler — "Oluştur" butonu görünürlüğü için (nitelik yoksa üretilecek kombinasyon yok).</summary>
    [Parameter, EditorRequired] public List<EntityAttributeGraphDto> Attributes { get; set; } = default!;

    /// <summary>Uzantı slot'u — varyant edit formuna entity-özel alanlar ekler (typed; ör. Good fiyat/stok). Boş = yok.</summary>
    [Parameter] public RenderFragment<TVariant>? ExtraFields { get; set; }

    /// <summary>Nitelik/değer değişince sahip layout'un OTOMATİK varyant senkronuna (regen + merge) bağlanır.
    /// Verilirse varyant gridinin toolbar'ına "Özellikleri Düzenle" butonu + nitelik POPUP'ı eklenir (nitelikler
    /// artık ayrı sekmede değil, varyant gridinin üstünden yönetilir). Boş = eski davranış (yalnız varyant gridi).</summary>
    [Parameter] public EventCallback OnAttributesChanged { get; set; }

    // "Özellikleri Düzenle" nitelik popup'ının görünürlüğü.
    private bool _attributesPopupVisible;

    // Varyant gridi toolbar'ına eklenen custom aksiyon — nitelik editörünü popup'ta açar (yalnız OnAttributesChanged bağlıysa).
    private IReadOnlyList<CrudToolbarAction>? AttributeActions =>
        OnAttributesChanged.HasDelegate
            ? new[]
            {
                new CrudToolbarAction
                {
                    SortIndex = 150,   // Sil(100) ile Arama(400) arası
                    Text = L["EditAttributes"].Value,
                    Tooltip = L["EditAttributes"].Value,
                    IconCssClass = TradeXpressIcons.Sliders,
                    OnClick = () => { _attributesPopupVisible = true; StateHasChanged(); return Task.CompletedTask; },
                },
            }
            : null;

    // "Uygula" — sahip host'un varyant regen'ini tetikler (VariantGraphMerge: mevcut varyant düzenlemeleri KORUNUR),
    // popup'ı kapatır (oluşan varyantlar gridde görünür). Nitelik/değer düzenlemesi bu ANA kadar regen ETMEZ.
    private async Task OnApplyAttributes()
    {
        await OnAttributesChanged.InvokeAsync();
        _attributesPopupVisible = false;
        StateHasChanged();
    }

    /// <summary>Çekirdek Stok Adedi kolonu + edit alanını göster. Stoğu ledger'dan (VoucherLine) türeten entity'ler
    /// (ör. Good) <c>false</c> geçer — statik stok anlamsız; pazaryeri push'lu entity'ler (Product) varsayılan <c>true</c>.</summary>
    [Parameter] public bool ShowStockQuantity { get; set; } = true;

    /// <summary>Net maliyet kolonu görünsün mü — VARSAYILAN KAPALI. Alan yalnız ürün varyantında (NetCost)
    /// mevcut; jenerik varyantı paylaşan diğer emtia ailelerinde boş kolon çizilmesin.</summary>
    [Parameter] public bool ShowNetCost { get; set; }

    /// <summary>
    /// Varyant KİMLİK alanları (Kod + Kombinasyon özeti) düzenleme formunda görünsün mü.
    ///
    /// <para><c>false</c> = TEK VARYANTLI ürün: tek bir varyant varken "hangi kombinasyon" sorusunun cevabı
    /// yok (özet zaten boş) ve kod otomatik üretiliyor — iki salt-okunur alan formu yormaktan başka iş
    /// görmüyordu (2026-07-28 Hakan).</para>
    /// </summary>
    [Parameter] public bool ShowIdentity { get; set; } = true;

    /// <summary>Grid'de ana varyantın kodu yerine gösterilecek metin — VERİLMEZSE sahibin kodu formun cascade
    /// ettiği modelden (<c>EditModel</c>, <see cref="IHasCode"/>) okunur; yani her varyantlı emtia formu bunu
    /// KENDİLİĞİNDEN alır (2026-08-15 Hakan: "diğer varyant barındıran tüm emtialarda DRY"). Açık parametre
    /// yalnız sahibin kodu modelin Code'u olmayan istisnai bir yüzey içindir. "ANAVARYANT" iç bir sabittir ve
    /// kullanıcı için anlam taşımıyor; sunucu da kayıtta ana varyantı sahibin koduna eşitler
    /// (EntityVariantSynchronizer.ApplyOwnerIdentity) — burası o kuralın kayıt-öncesi aynasıdır.</summary>
    [Parameter] public string? MainVariantCodeDisplay { get; set; }

    /// <summary>Formun düzenlediği model — sahibin kodu buradan (EntityEditForm cascade'i).</summary>
    [CascadingParameter(Name = "EditModel")] private object? EditModel { get; set; }

    // Sahibin kodunun panelin en son gördüğü hâli — ana varyantın "izleme" kararı için (aşağıda).
    private string? _lastOwnerCode;

    /// <summary>
    /// ANA VARYANTIN KODU SAHİBİN KODUNU İZLER (2026-08-15 Hakan: "yeni emtia tanımlanırsa ana varyant kodla
    /// aynı olacak şekilde açılsın") — DRY: dört host da yeni kayıtta ana varyantı "ANAVARYANT" sabitiyle
    /// seed'ler (kod o an henüz yazılmamıştır); host başına kopyalamak yerine panel, model kodu her değiştiğinde
    /// ana varyantın kodunu ona eşitler. Kullanıcı ana varyanta ELLE başka bir kod yazdıysa (ne sentinel ne
    /// eski sahip kodu) DOKUNULMAZ — o meşru bir düzenlemedir. Sunucu kayıtta zaten aynı eşitlemeyi yapar
    /// (<c>EntityVariantSynchronizer.ApplyOwnerIdentity</c>); burası kayıt-öncesi aynasıdır.
    /// </summary>
    /// <summary>Formun EditContext'i — sahibin KOD ALANI değişince haberdar olmak için. Cascade edilen model
    /// nesnesi aynı referans kaldığından (içi değişir) <c>OnParametersSet</c> tetiklenmez; DevExpress editörleri
    /// <c>OnFieldChanged</c>'i tetikler, izleme oradan yürür (ilk sürüm yalnız OnParametersSet'e dayanıyordu ve
    /// kod yazılınca hiç koşmuyordu — Hakan'ın "+ ile açtım, kodu göremedim" tespiti).</summary>
    [CascadingParameter] private EditContext? FormEditContext { get; set; }

    private EditContext? _subscribedEditContext;

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        SubscribeToOwnerCodeChanges();
        FollowOwnerCode();
    }

    private void SubscribeToOwnerCodeChanges()
    {
        if (ReferenceEquals(_subscribedEditContext, FormEditContext))
        {
            return;
        }

        if (_subscribedEditContext is not null)
        {
            _subscribedEditContext.OnFieldChanged -= OnOwnerFieldChanged;
        }

        _subscribedEditContext = FormEditContext;
        if (_subscribedEditContext is not null)
        {
            _subscribedEditContext.OnFieldChanged += OnOwnerFieldChanged;
        }
    }

    private void OnOwnerFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        // Yalnız SAHİBİN Code alanı ilgilendirir; başka alan değişimleri panelin işi değil.
        if (!string.Equals(e.FieldIdentifier.FieldName, nameof(IHasCode.Code), StringComparison.Ordinal)
            || !ReferenceEquals(e.FieldIdentifier.Model, EditModel))
        {
            return;
        }

        FollowOwnerCode();
        StateHasChanged();
    }

    public void Dispose()
    {
        if (_subscribedEditContext is not null)
        {
            _subscribedEditContext.OnFieldChanged -= OnOwnerFieldChanged;
        }
    }

    private void FollowOwnerCode()
    {
        var ownerCode = MainVariantCodeDisplay ?? (EditModel as IHasCode)?.Code;
        if (string.IsNullOrWhiteSpace(ownerCode) || string.Equals(ownerCode, _lastOwnerCode, StringComparison.Ordinal))
        {
            return;
        }

        var main = Variants?.FirstOrDefault(v => v.IsMain && !v.IsDeleted);
        if (main is not null && IsFollowingOwner(main.Code))
        {
            main.Code = ownerCode;
        }

        _lastOwnerCode = ownerCode;
    }

    // Kod hâlâ "sahibi izliyor" mu: sentinel, boş ya da sahibin bir önceki kodu → evet; kullanıcı yazımı → hayır.
    private bool IsFollowingOwner(string? variantCode)
    {
        return string.IsNullOrWhiteSpace(variantCode)
            || string.Equals(variantCode, EntityVariantConsts.MainVariantCode, StringComparison.OrdinalIgnoreCase)
            || (_lastOwnerCode is not null && string.Equals(variantCode, _lastOwnerCode, StringComparison.Ordinal));
    }

    /// <summary>Satırın gösterilecek kodu — ana varyantta sahibin kodu (açık parametre ?? cascade model kodu).</summary>
    private string CodeTextOf(TVariant variant)
    {
        if (!variant.IsMain)
        {
            return variant.Code;
        }

        var ownerCode = MainVariantCodeDisplay ?? (EditModel as IHasCode)?.Code;
        return string.IsNullOrWhiteSpace(ownerCode) ? variant.Code : ownerCode;
    }

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
