using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Services;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>
/// Kanal "Giderler" editörü code-behind — <c>EntryPanelBase&lt;SideCostItemDto&gt;</c> türevi (ProductRecipePanel
/// deseni: toolbar → buffered draft paneli → grid). Lookup verisi (Service/Account/SubAccount/CurrencyUnit) TEK
/// yerden yüklenir; Service/Account "ekle/düzelt" STANDART popup+refresh+odak akışıyla (IViewOpener → EditHost).
/// Liste BOŞSA kanal tipine göre varsayılan tohum önerilir (<see cref="SideCostItemDefaults"/> — yeni kanal
/// kaydı dahil). N11'de gömülü komisyon TSV importu buradan tetiklenir (host-only uç; sorunlu satırlar raporda
/// GÖSTERİLİR — sessiz geçilmez).
/// </summary>
public partial class SideCostSettingsEditor
{
    [Parameter, EditorRequired] public SideCostSettingsDto Settings { get; set; } = default!;

    /// <summary>Kanal türü — varsayılan tohum + komisyon ipucu + N11 import düğmesi bunu kullanır.</summary>
    [Parameter] public SalesChannelType ChannelType { get; set; }

    /// <summary>Varsayılan tohum önerilsin mi — yalnız ayar HİÇ yapılandırılmamışken (DB'de null) true.
    /// Kullanıcı tüm satırları silip kaydettiyse ({"Items":[]}) "gider yok" KARARDIR — yeniden tohumlanmaz
    /// (SideCostPlan.From'daki null/boş ayrımının UI aynası). Ayrımı layout verir (null→boş DTO dönüşümünden önce).</summary>
    [Parameter] public bool SuggestDefaults { get; set; }

    [Inject] private IServiceAppService ServiceAppService { get; set; } = default!;
    [Inject] private IAccountAppService AccountAppService { get; set; } = default!;
    [Inject] private ISubAccountAppService SubAccountAppService { get; set; } = default!;
    [Inject] private ILookupCache<CurrencyUnitListDto> CurrencyLookup { get; set; } = default!;
    [Inject] private IN11CategoryAppService N11CategoryAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IViewOpener ViewOpener { get; set; } = default!;
    [Inject] private IPopupService PopupService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private IReadOnlyList<ServiceListDto> _services = Array.Empty<ServiceListDto>();
    private List<AccountListDto> _accounts = new();
    private List<SubAccountListDto> _subAccounts = new();
    private IReadOnlyList<CurrencyUnitListDto> _currencyUnits = Array.Empty<CurrencyUnitListDto>();
    private IReadOnlyList<SideCostKindItem> _kindItems = Array.Empty<SideCostKindItem>();
    private IReadOnlyList<SideCostCalcModeItem> _calcModeItems = Array.Empty<SideCostCalcModeItem>();
    private IReadOnlyList<SideCostPostingModeItem> _postingModeItems = Array.Empty<SideCostPostingModeItem>();

    private bool _isMobile;
    private SideCostItemDto? _selectedItem;
    private bool _importing;
    private N11CommissionImportResultDto? _importResult;
    private bool _popupSaved;

    private bool IsN11
    {
        get { return ChannelType == SalesChannelType.TrN11; }
    }

    /// <summary>Grid satırları — DisplayOrder sırasıyla (GrossUp-en-sonda kuralı MOTORDA; grid kullanıcı sırasını gösterir).</summary>
    private IEnumerable<SideCostItemDto> OrderedItems
    {
        get { return Settings.Items.OrderBy(i => i.DisplayOrder); }
    }

    /// <summary>Kanala göre komisyon açıklaması: N11 → kategoriden otomatik (satır oranı fallback); diğerleri → doğrudan oran.</summary>
    private string CommissionHint
    {
        get
        {
            return ChannelType switch
            {
                SalesChannelType.TrN11 => L["SideCost:CommissionHintN11"].Value,
                SalesChannelType.Etsy => L["SideCost:CommissionHintEtsy"].Value,
                _ => L["SideCost:CommissionHintDefault"].Value,
            };
        }
    }

    /// <summary>Import raporunun sorunlu satırları (eşleşmeyen + çakışan + geçersiz) — memo'da alt alta gösterilir.</summary>
    private List<string> ImportIssueLines
    {
        get
        {
            if (_importResult is not { } r)
            {
                return new List<string>();
            }

            return r.UnmatchedRows.Concat(r.ConflictRows).Concat(r.InvalidRows).ToList();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        _kindItems = new List<SideCostKindItem>
        {
            new(SideCostKind.Packaging, KindLabel(SideCostKind.Packaging)),
            new(SideCostKind.Cargo, KindLabel(SideCostKind.Cargo)),
            new(SideCostKind.InsuredShipping, KindLabel(SideCostKind.InsuredShipping)),
            new(SideCostKind.Commission, KindLabel(SideCostKind.Commission)),
            new(SideCostKind.ChannelFixed, KindLabel(SideCostKind.ChannelFixed)),
        };
        _calcModeItems = new List<SideCostCalcModeItem>
        {
            new(SideCostCalcMode.FixedAmount, L["SideCost:CalcMode:FixedAmount"].Value),
            new(SideCostCalcMode.PercentOfCost, L["SideCost:CalcMode:PercentOfCost"].Value),
            new(SideCostCalcMode.GrossUpPercent, L["SideCost:CalcMode:GrossUpPercent"].Value),
        };
        _postingModeItems = new List<SideCostPostingModeItem>
        {
            new(SideCostPostingMode.CounterpartyAccount, L["SideCost:PostingMode:CounterpartyAccount"].Value),
            new(SideCostPostingMode.Expense, L["SideCost:PostingMode:Expense"].Value),
        };

        await LoadLookupsAsync();
        SeedDefaultsIfEmpty();
    }

    // ── EntryPanelBase sözleşmesi (buffered yaşam döngüsü Framework tabanında) ──────────────────────

    protected override IList<SideCostItemDto> ItemsSource
    {
        get { return Settings.Items; }
    }

    protected override SideCostItemDto CloneItem(SideCostItemDto s)
    {
        return new SideCostItemDto
        {
            Kind = s.Kind,
            DisplayName = s.DisplayName,
            CalcMode = s.CalcMode,
            Value = s.Value,
            CurrencyUnitId = s.CurrencyUnitId,
            ServiceId = s.ServiceId,
            PostingMode = s.PostingMode,
            AccountId = s.AccountId,
            SubAccountId = s.SubAccountId,
            AutoRate = s.AutoRate,
            IsEnabled = s.IsEnabled,
            DisplayOrder = s.DisplayOrder,
            RequiresVariantOptIn = s.RequiresVariantOptIn,
        };
    }

    protected override void ApplyDraft(SideCostItemDto d, SideCostItemDto target)
    {
        target.Kind = d.Kind;
        target.DisplayName = d.DisplayName;
        target.CalcMode = d.CalcMode;
        target.Value = d.Value;
        target.CurrencyUnitId = d.CurrencyUnitId;
        target.ServiceId = d.ServiceId;
        target.PostingMode = d.PostingMode;
        target.AccountId = d.AccountId;
        target.SubAccountId = d.SubAccountId;
        target.AutoRate = d.AutoRate;
        target.IsEnabled = d.IsEnabled;
        target.DisplayOrder = d.DisplayOrder;
        target.RequiresVariantOptIn = d.RequiresVariantOptIn;
    }

    protected override SideCostItemDto CreateNextDraft(SideCostItemDto saved)
    {
        // Seri giriş kullanılmıyor (Kaydet paneli kapatır) — sözleşme gereği aynı türde taze draft döner.
        return BuildDraft(saved.Kind);
    }

    // ── Toolbar / panel akışı ───────────────────────────────────────────────────────────────────────

    /// <summary>"Yeni" dropdown'ı — seçilen türde draft açar (tür-varsayılanları: paketleme genel gider,
    /// komisyon GrossUp + N11'de AutoRate, sigortalı gönderim varyant opt-in).</summary>
    private void OpenItemDraft(SideCostKind kind)
    {
        OpenDraft(BuildDraft(kind));
    }

    private SideCostItemDto BuildDraft(SideCostKind kind)
    {
        return new SideCostItemDto
        {
            Kind = kind,
            CalcMode = kind == SideCostKind.Commission ? SideCostCalcMode.GrossUpPercent : SideCostCalcMode.FixedAmount,
            PostingMode = kind == SideCostKind.Packaging ? SideCostPostingMode.Expense : SideCostPostingMode.CounterpartyAccount,
            AutoRate = kind == SideCostKind.Commission && DefaultAutoRate,
            RequiresVariantOptIn = kind == SideCostKind.InsuredShipping,
            DisplayOrder = NextDisplayOrder(),
        };
    }

    private int NextDisplayOrder()
    {
        return Settings.Items.Count == 0 ? 0 : Math.Min(Settings.Items.Max(i => i.DisplayOrder) + 1, EntityFieldConsts.DisplayOrderMax);
    }

    private void EditSelectedItem()
    {
        if (_selectedItem is { } item)
        {
            BeginEdit(item);
        }
    }

    private async Task DeleteSelectedItemAsync()
    {
        if (_selectedItem is not { } item)
        {
            return;
        }

        Settings.Items.Remove(item);
        _selectedItem = null;
        await NotifyItemRemovedAsync(item);
    }

    /// <summary>Kaydet: hafif ön-doğrulama (moda göre değer sınırı — domain guard'ının dostane aynası),
    /// draft'ı uygula ve PANELİ KAPAT (gider listesi kısa — seri giriş yerine kapan-dön daha akıcı).</summary>
    private async Task SaveAndCloseAsync()
    {
        if (Draft is { } d && !ValidateDraft(d))
        {
            return;
        }

        await SaveDraftAsync();
        CloseDraft();
    }

    // Moda göre değer sınırları — domain VO ctor guard'larının istemci aynası (sunucu yine fail-fast).
    private bool ValidateDraft(SideCostItemDto d)
    {
        if (d.CalcMode == SideCostCalcMode.FixedAmount && d.Value < 0m)
        {
            UiService.ShowErrorToast(L["TradeXpress:SalesChannel:SideCostAmountNegative"].Value);
            return false;
        }

        if ((d.CalcMode == SideCostCalcMode.PercentOfCost && (d.Value < 0m || d.Value > 100m))
            || (d.CalcMode == SideCostCalcMode.GrossUpPercent
                && (d.Value < 0m || d.Value >= ProductRecipeConsts.GrossUpOperandExclusiveMax)))
        {
            UiService.ShowErrorToast(L["TradeXpress:SalesChannel:SideCostRateOutOfRange"].Value);
            return false;
        }

        // Opt-in kalem GrossUp olamaz (domain guard aynası — composer birleşik GrossUp satırı toggle senkronuyla uyumsuz).
        if (d.RequiresVariantOptIn && d.CalcMode == SideCostCalcMode.GrossUpPercent)
        {
            UiService.ShowErrorToast(L["TradeXpress:SalesChannel:SideCostOptInGrossUpNotSupported"].Value);
            return false;
        }

        if (d.IsEnabled && d.CalcMode == SideCostCalcMode.GrossUpPercent)
        {
            // Aktif GrossUp TOPLAMI payda sınırını aşamaz (SideCostSettings ctor Σ-guard'ının dostane aynası —
            // kalem tek başına geçerli olsa da toplam taşarsa form Save'inde patlamasın, burada söylensin).
            var otherGrossUpTotal = Settings.Items
                .Where(i => !ReferenceEquals(i, EditingItem) && i.IsEnabled && i.CalcMode == SideCostCalcMode.GrossUpPercent)
                .Sum(i => i.Value);
            if (otherGrossUpTotal + d.Value >= ProductRecipeConsts.GrossUpOperandExclusiveMax)
            {
                UiService.ShowErrorToast(L["TradeXpress:SalesChannel:SideCostRateOutOfRange"].Value);
                return false;
            }

            // En fazla 1 aktif AutoRate kalemi (SideCostSettings ctor guard aynası — çözülmüş oran 2x sayılmasın).
            if (d.AutoRate && Settings.Items.Any(i => !ReferenceEquals(i, EditingItem) && i.IsEnabled && i.AutoRate))
            {
                UiService.ShowErrorToast(L["TradeXpress:SalesChannel:SideCostSingleAutoRateItem"].Value);
                return false;
            }
        }

        return true;
    }

    private void OnSelectedItemChanged(object item)
    {
        _selectedItem = item as SideCostItemDto;
    }

    // ── Draft alan değişimleri (cascade temizlikleri — yetim değer bırakma) ─────────────────────────

    private void OnKindChanged(SideCostKind kind)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.Kind = kind;
        if (kind == SideCostKind.Commission)
        {
            d.CalcMode = SideCostCalcMode.GrossUpPercent;   // komisyon ZORUNLU GrossUp (kâr korunumu)
            d.CurrencyUnitId = null;
            d.AutoRate = DefaultAutoRate;
        }
        else
        {
            d.AutoRate = false;   // AutoRate yalnız komisyonda anlamlı
        }
    }

    /// <summary>Yeni komisyon draft'ının AutoRate varsayılanı: N11'de AÇIK — ama listede zaten aktif AutoRate
    /// kalemi varsa KAPALI (en fazla 1 aktif AutoRate — domain guard'ına kullanıcıyı bilerek yürütmeyelim;
    /// düzenlemede kendisi hariç sayılır).</summary>
    private bool DefaultAutoRate
    {
        get
        {
            return IsN11 && !Settings.Items.Any(i => !ReferenceEquals(i, EditingItem) && i.IsEnabled && i.AutoRate);
        }
    }

    private void OnCalcModeChanged(SideCostCalcMode mode)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.CalcMode = mode;
        if (mode != SideCostCalcMode.FixedAmount)
        {
            d.CurrencyUnitId = null;   // birim yalnız sabit tutarda anlamlı
        }
    }

    private void OnDisplayNameChanged(string value)
    {
        if (Draft is { } d)
        {
            d.DisplayName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    // Genel gidere geçince cari referansları temizlenir (yetim id bırakma — fail-fast VO guard'ına takılmasın).
    private void OnPostingModeChanged(SideCostPostingMode mode)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.PostingMode = mode;
        if (mode == SideCostPostingMode.Expense)
        {
            d.AccountId = null;
            d.SubAccountId = null;
        }
    }

    // Ana hesap değişince alt hesap sıfırlanır (cascade; ana-hesapsız alt hesap domain guard'ına takılır).
    private void OnAccountChanged(Guid? accountId)
    {
        if (Draft is not { } d)
        {
            return;
        }

        d.AccountId = accountId;
        d.SubAccountId = null;
    }

    /// <summary>Alt hesap cascade'i — yalnız seçili ana hesabın alt hesapları (AccountSelectionPanel paritesi).</summary>
    private IEnumerable<SubAccountListDto> FilteredSubAccounts(SideCostItemDto d)
    {
        return d.AccountId is { } acc
            ? _subAccounts.Where(s => s.AccountId == acc)
            : Enumerable.Empty<SubAccountListDto>();
    }

    // ── Görüntü yardımcıları (grid hücreleri + panel başlığı) ───────────────────────────────────────

    private string EditorTitle(SideCostItemDto d)
    {
        return $"{L["SideCosts"]}: {DisplayNameOf(d)}";
    }

    private string KindLabel(SideCostKind kind)
    {
        return kind switch
        {
            SideCostKind.Packaging => L["SideCost:Kind:Packaging"].Value,
            SideCostKind.Cargo => L["SideCost:Kind:Cargo"].Value,
            SideCostKind.InsuredShipping => L["SideCost:Kind:InsuredShipping"].Value,
            SideCostKind.Commission => L["SideCost:Kind:Commission"].Value,
            SideCostKind.ChannelFixed => L["SideCost:Kind:ChannelFixed"].Value,
            _ => kind.ToString(),
        };
    }

    /// <summary>Görünen ad — boşsa türün lokalizesi (kullanıcı kararı: DisplayName serbest, boş bırakılabilir).</summary>
    private string DisplayNameOf(SideCostItemDto item)
    {
        return string.IsNullOrWhiteSpace(item.DisplayName) ? KindLabel(item.Kind) : item.DisplayName!;
    }

    private string CalcModeLabel(SideCostItemDto item)
    {
        var label = item.CalcMode switch
        {
            SideCostCalcMode.PercentOfCost => L["SideCost:CalcMode:PercentOfCost"].Value,
            SideCostCalcMode.GrossUpPercent => L["SideCost:CalcMode:GrossUpPercent"].Value,
            _ => L["SideCost:CalcMode:FixedAmount"].Value,
        };

        // Komisyonda AutoRate açıkken oranın kategoriden çözüldüğünü hücrede belli et (Value = fallback).
        return item.Kind == SideCostKind.Commission && item.AutoRate
            ? $"{label} ({L["SideCost:AutoRateShort"].Value})"
            : label;
    }

    private string UnitLabelOf(SideCostItemDto item)
    {
        if (item.CalcMode != SideCostCalcMode.FixedAmount)
        {
            return "%";
        }

        return _currencyUnits.FirstOrDefault(c => c.Id == item.CurrencyUnitId)?.Code
            ?? L["SideCost:LocalCurrency"].Value;
    }

    private string ServiceCodeOf(SideCostItemDto item)
    {
        return _services.FirstOrDefault(s => s.Id == item.ServiceId)?.Code ?? string.Empty;
    }

    private string PostingLabelOf(SideCostItemDto item)
    {
        if (item.PostingMode == SideCostPostingMode.Expense)
        {
            return L["SideCost:PostingMode:Expense"].Value;
        }

        var accountCode = _accounts.FirstOrDefault(a => a.Id == item.AccountId)?.Code;
        return accountCode is null
            ? L["SideCost:PostingMode:CounterpartyAccount"].Value
            : $"{L["SideCost:PostingMode:CounterpartyAccount"]}: {accountCode}";
    }

    // ── Lookup yükleme + varsayılan tohum ───────────────────────────────────────────────────────────

    private async Task LoadLookupsAsync()
    {
        _services = await ServiceAppService.GetPickerListAsync();
        await ReloadAccountsAsync();
        await ReloadSubAccountsAsync();
        _currencyUnits = await CurrencyLookup.GetAsync();
    }

    /// <summary>Ayar HİÇ yapılandırılmamışsa (SuggestDefaults — DB'de null; yeni kanal kaydı dahil) kanal tipine
    /// göre öneri satırları doldurulur — kullanıcı düzenler/siler, zorlama yok. Bilerek boşaltılmış kayıt
    /// ({"Items":[]}) yeniden TOHUMLANMAZ. Etsy satış-başı sabiti için USD birimi lookup'tan çözülür.</summary>
    private void SeedDefaultsIfEmpty()
    {
        if (!SuggestDefaults || Settings.Items.Count > 0)
        {
            return;
        }

        var usdId = _currencyUnits.FirstOrDefault(c => c.Code == "USD")?.Id;
        Settings.Items.AddRange(SideCostItemDefaults.Build(ChannelType, usdId));
    }

    private async Task ReloadAccountsAsync()
    {
        var result = await AccountAppService.GetListAsync(new AccountListRequestDto { MaxResultCount = 1000 });
        _accounts = result.Items.ToList();
    }

    private async Task ReloadSubAccountsAsync()
    {
        var result = await SubAccountAppService.GetListAsync(new SubAccountListRequestDto { MaxResultCount = 1000 });
        _subAccounts = result.Items.ToList();
    }

    // ── Service ekle/düzelt — STANDART popup+refresh+odak (ViewOpener → ServiceEditHost) ──

    private async Task<Guid?> OnServiceAddAsync()
    {
        var beforeIds = _services.Select(s => s.Id).ToHashSet();
        if (!await OpenPopupAsync(typeof(Services.ServiceEditHost), null, L["Service"].Value, TradeXpressIcons.Service))
        {
            return null;
        }

        _services = await ServiceAppService.GetPickerListAsync();
        var newId = _services.FirstOrDefault(s => !beforeIds.Contains(s.Id))?.Id;
        if (newId is { } id && Draft is { } d)
        {
            d.ServiceId = id;
        }

        await InvokeAsync(StateHasChanged);
        return newId;
    }

    private async Task OnServiceEditAsync(Guid? serviceId)
    {
        if (serviceId is not { } id || id == Guid.Empty)
        {
            return;
        }

        var service = _services.FirstOrDefault(s => s.Id == id);
        var title = service is not null ? $"{L["Service"]}: {service.Code}" : L["Service"].Value;
        if (!await OpenPopupAsync(typeof(Services.ServiceEditHost), id, title, TradeXpressIcons.Service))
        {
            return;
        }

        _services = await ServiceAppService.GetPickerListAsync();
        await InvokeAsync(StateHasChanged);
    }

    // ── Account ekle/düzelt — STANDART popup+refresh+odak (ViewOpener → AccountEditHost) ──

    private async Task<Guid?> OnAccountAddAsync()
    {
        var beforeIds = _accounts.Select(a => a.Id).ToHashSet();
        if (!await OpenPopupAsync(typeof(Accounts.AccountEditHost), null, L["Account"].Value, TradeXpressIcons.Account))
        {
            return null;
        }

        await ReloadAccountsAsync();
        await ReloadSubAccountsAsync();
        var newId = _accounts.FirstOrDefault(a => !beforeIds.Contains(a.Id))?.Id;
        if (newId is { } id)
        {
            OnAccountChanged(id);
        }

        await InvokeAsync(StateHasChanged);
        return newId;
    }

    private async Task OnAccountEditAsync(Guid? accountId)
    {
        if (accountId is not { } id || id == Guid.Empty)
        {
            return;
        }

        var account = _accounts.FirstOrDefault(a => a.Id == id);
        var title = account is not null ? $"{L["Account"]}: {account.Code}" : L["Account"].Value;
        if (!await OpenPopupAsync(typeof(Accounts.AccountEditHost), id, title, TradeXpressIcons.Account))
        {
            return;
        }

        await ReloadAccountsAsync();
        await ReloadSubAccountsAsync();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Popup açar; kullanıcı KAYDETTİYSE true (iptalde false → çağıran tazeleme yapmaz).</summary>
    private async Task<bool> OpenPopupAsync(Type editHostType, Guid? id, string title, string icon)
    {
        _popupSaved = false;
        await ViewOpener.OpenAsync(editHostType, id, title, icon, new Dictionary<string, object>
        {
            { "OnSaved", EventCallback.Factory.Create(this, () => { _popupSaved = true; PopupService.Close(); }) },
            { "OnClosed", EventCallback.Factory.Create(this, () => PopupService.Close()) },
        });
        return _popupSaved;
    }

    // ── N11 komisyon importu (gömülü TSV → N11Category.SetCommission; host-only uç) ──

    private async Task ImportCommissionsAsync()
    {
        _importing = true;
        try
        {
            _importResult = await N11CategoryAppService.ImportCommissionsAsync();
            UiService.ShowSuccessToast(L["SideCost:ImportCompleted"].Value);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
        finally
        {
            _importing = false;
        }
    }

    // Ortak panel stilleri (ProcessPanelStyles SSOT — süreç panelleriyle AYNI görünüm).
    private string GroupStyle()
    {
        return ProcessPanelStyles.Group(_isMobile);
    }

    private string GroupStyle(int w)
    {
        return ProcessPanelStyles.Group(_isMobile, w);
    }
}

/// <summary>Kalem türü combo öğesi (lokalize etiket).</summary>
public sealed record SideCostKindItem(SideCostKind Value, string Label);

/// <summary>Hesaplama modu combo öğesi (lokalize etiket).</summary>
public sealed record SideCostCalcModeItem(SideCostCalcMode Value, string Label);

/// <summary>Fişleme modu combo öğesi (lokalize etiket).</summary>
public sealed record SideCostPostingModeItem(SideCostPostingMode Value, string Label);
