using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Components;
using Volo.Abp;

namespace Integration.TradeXpress.Blazor.Client.Pages.Goods;

/// <summary>Mamül edit host — ince sarmal (coordinator + lookup listeleri kurar, geri kalan CrudEditHost'ta).
/// DUMB layout servis çağırmaz → tedarikçi cari (Account/SubAccount) + para birimi listelerini host yükler.</summary>
public partial class GoodEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    private List<CurrencyUnitListDto> _units = new();
    private List<AccountListDto> _accounts = new();
    private List<SubAccountListDto> _subAccounts = new();
    private Guid? _localCurrencyUnitId;   // yeni kayıt alış birimi default'u = working şirket ülke parası
    private ICommitCoordinator<GoodGetDto, GoodListDto, Guid, GoodListRequestDto>? _coordinator;
    private bool _ready;

    protected override async Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<GoodGetDto, GoodListDto, Guid, GoodListRequestDto, GoodCreateDto, GoodUpdateDto>(
            GoodAppService, Mapper);

        await Working.EnsureLoadedAsync();

        var units = await CurrencyUnitAppService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _units = units.Items.ToList();

        // Tedarikçi cari — company-scope hesaplar + working şubenin alt hesapları (AccountSelectionPanel deseni).
        _accounts = await LoadAccountsAsync();
        _subAccounts = await LoadSubAccountsAsync();

        // Yeni mamül alış fiyatı birimi = working şirketin YEREL (ülke) parası (TR→TRY, US→USD).
        _localCurrencyUnitId = await EffectivePriceAppService.GetWorkingLocalCurrencyUnitIdAsync();

        _ready = true;
    }

    // Yeni mamül default'ları — perakende varsayılanları (adet-bazlı, KDV %10 dahil, alış birimi yerel para).
    private void ApplyNewDefaults(GoodGetDto m)
    {
        m.IsActive = true;
        m.PriceTypeChange = true;
        m.CompanyId = Working.CurrentCompanyId;
        m.IsQuantity = true;                           // adet-bazlı default
        m.VatPurchaseRate = 10m;                       // KDV alış default %10
        m.VatSaleRate = 10m;                           // KDV satış default %10

        // Standart ANA VARYANT — yeni kayıtta hazır (kullanıcı nitelik eklemeden fiyat/barkod girebilsin). Nitelik×değer
        // üretilince (GenerateVariants) bu liste değişir; üretilmezse save'de synchronizer bu main'i kalıcılaştırır
        // (IsMain + boş CombinationKey → server main'e eşlenir). Fiyat birimi/KDV default varyant seviyesinde.
        m.Variants.Add(new GoodVariantGraphDto
        {
            IsMain = true,
            Code = EntityVariantConsts.MainVariantCode,
            Name = EntityVariantConsts.MainVariantName,
            IsActive = true,
            EntryPriceUnitId = _localCurrencyUnitId,
        });
    }

    // Lookup ekle/düzelt (LookupComboBox EditComponentType + EntityChange) sonrası tazeleme — listeyi yeniden
    // çeker + re-render (yeni kayıt combo'da görünür; auto-select EntityChange ile).
    // Hesap tazelemesi ALT HESAPLARI DA çeker: AccountEditHost içindeki alt hesap drill'i (+ varsayılan ANAHESAP)
    // yeni hesabın alt hesaplarını yazar; bayat kalırsa cascade combo boş görünürdü.
    private async Task ReloadAccountsAsync()
    {
        _accounts = await LoadAccountsAsync();
        _subAccounts = await LoadSubAccountsAsync();
        StateHasChanged();
    }

    private async Task ReloadSubAccountsAsync()
    {
        _subAccounts = await LoadSubAccountsAsync();
        StateHasChanged();
    }

    private async Task<List<AccountListDto>> LoadAccountsAsync()
    {
        var result = await AccountAppService.GetListAsync(new AccountListRequestDto { MaxResultCount = 1000 });
        return result.Items.ToList();
    }

    private async Task<List<SubAccountListDto>> LoadSubAccountsAsync()
    {
        var result = await SubAccountAppService.GetListAsync(
            new SubAccountListRequestDto { BranchId = Working.CurrentBranchId, MaxResultCount = 1000 });
        return result.Items.ToList();
    }

    private async Task ReloadCurrencyUnitsAsync()
    {
        var units = await CurrencyUnitAppService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _units = units.Items.ToList();
        StateHasChanged();
    }

    // "Varyantları Oluştur" — DUMB layout servis çağırmaz, çağrıyı host yapar. PERSISTSİZ önizleme: sunucu nitelik
    // grafından kartezyeni hesaplar (jenerik EntityVariantGraphService), dönen graf Model.Variants'a yazılır (Save'de kalıcı).
    private async Task GenerateVariantsAsync(GoodGetDto model)
    {
        if (VariantGraphMerge.HasIncompleteAttribute(model.Attributes))
        {
            return;
        }

        try
        {
            var generated = await GoodAppService.GenerateVariantsAsync(new EntityVariantGenerateRequestDto
            {
                OwnerName = model.Name,
                Attributes = model.Attributes,
            });

            // Para birimi = mamül takip birimi (working şirket ülke parası) — üretilen her varyanta default (kullanıcı değiştirebilir).
            foreach (var g in generated)
            {
                g.EntryPriceUnitId ??= _localCurrencyUnitId;
            }

            VariantGraphMerge.Apply(model.Variants, generated);
        }
        catch (BusinessException bex)
        {
            // In-process BusinessException lokalize OLMAZ (Blazor Server) → kodu resource'tan çevir.
            UiService.ShowErrorToast(L[bex.Code ?? bex.Message].Value);
        }
    }
}
