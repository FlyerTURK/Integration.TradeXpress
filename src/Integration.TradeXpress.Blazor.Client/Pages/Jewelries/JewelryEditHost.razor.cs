using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Components;
using Volo.Abp;

namespace Integration.TradeXpress.Blazor.Client.Pages.Jewelries;

/// <summary>Mücevher edit host — ince sarmal (coordinator + para birimi listesi + "Varyantları Oluştur" delegesi).
/// DUMB layout servis çağırmaz → lookup + varyant üretimi host'ta (Good deseni).</summary>
public partial class JewelryEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    private List<CurrencyUnitListDto> _units = new();
    private ICommitCoordinator<JewelryGetDto, JewelryListDto, Guid, JewelryListRequestDto>? _coordinator;
    private bool _ready;

    protected override async Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<JewelryGetDto, JewelryListDto, Guid, JewelryListRequestDto, JewelryCreateDto, JewelryUpdateDto>(
            JewelryAppService, Mapper);

        await Working.EnsureLoadedAsync();

        var result = await CurrencyUnitAppService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _units = result.Items.ToList();

        _ready = true;
    }

    // Yeni mücevher default'ları — working şirket + standart ana varyant (kullanıcı nitelik eklemeden barkod/GTIN girebilsin).
    private void ApplyNewDefaults(JewelryGetDto m)
    {
        m.IsActive = true;
        m.PriceTypeChange = true;
        m.CompanyId = Working.CurrentCompanyId;

        // Nitelik×değer üretilince (GenerateVariants) liste değişir; üretilmezse save'de synchronizer bu main'i kalıcılaştırır
        // (IsMain + boş CombinationKey → server main'e eşlenir).
        m.Variants.Add(new EntityVariantGraphDto
        {
            IsMain = true,
            Code = EntityVariantConsts.MainVariantCode,
            Name = EntityVariantConsts.MainVariantName,
            IsActive = true,
        });
    }

    // "Varyantları Oluştur" — DUMB layout servis çağırmaz, çağrıyı host yapar. PERSISTSİZ önizleme: sunucu nitelik
    // grafından kartezyeni hesaplar (jenerik EntityVariantGraphService), dönen graf Model.Variants'a yazılır (Save'de kalıcı).
    private async Task GenerateVariantsAsync(JewelryGetDto model)
    {
        try
        {
            var generated = await JewelryAppService.GenerateVariantsAsync(new EntityVariantGenerateRequestDto
            {
                OwnerName = model.Name,
                Attributes = model.Attributes,
            });

            model.Variants.Clear();
            model.Variants.AddRange(generated);
        }
        catch (BusinessException bex)
        {
            // In-process BusinessException lokalize OLMAZ (Blazor Server) → kodu resource'tan çevir.
            UiService.ShowErrorToast(L[bex.Code ?? bex.Message].Value);
        }
    }
}
