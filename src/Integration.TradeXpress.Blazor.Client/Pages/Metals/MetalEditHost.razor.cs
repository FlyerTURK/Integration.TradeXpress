using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Components;
using Volo.Abp;

namespace Integration.TradeXpress.Blazor.Client.Pages.Metals;

/// <summary>Maden edit host — ince sarmal (coordinator + para birimi listesi + "Varyantları Oluştur" delegesi).
/// DUMB layout servis çağırmaz → lookup + varyant üretimi host'ta (Good/Jewelry deseni).</summary>
public partial class MetalEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }

    /// <summary>ÇAĞRI-BAŞI footer daraltma (2026-08-06 Hakan kararı) — gerekçe GoodEditHost'ta.</summary>
    [Parameter] public bool SupportsSaveAndNew { get; set; } = true;

    [Parameter] public bool SupportsDelete { get; set; } = true;

    /// <summary>Sınıflandırma panelinden ÖN-DOLDURMA (2026-08-07 U1 — GoodEditHost deseni). Panel bu formu
    /// <c>IViewOpener.OpenAsync</c>'in <c>extraParams</c>'ıyla açarken kod/ad geçiriyor; bu parametreler
    /// tanımlı OLMADIĞINDA <c>DynamicComponent</c> bilinmeyen-parametre <c>InvalidOperationException</c>'ı
    /// fırlatıp circuit'i DÜŞÜRÜYORDU. Boş geçilirse davranış eskisi gibi.</summary>
    [Parameter] public string? SeedCode { get; set; }

    [Parameter] public string? SeedName { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    private List<CurrencyUnitListDto> _units = new();
    private ICommitCoordinator<MetalGetDto, MetalListDto, Guid, MetalListRequestDto>? _coordinator;
    private bool _ready;

    protected override async Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<MetalGetDto, MetalListDto, Guid, MetalListRequestDto, MetalCreateDto, MetalUpdateDto>(
            MetalAppService, Mapper);

        var result = await CurrencyUnitAppService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _units = result.Items.ToList();

        _ready = true;
    }

    // Yeni maden default'ları + standart ana varyant (kullanıcı nitelik eklemeden barkod/GTIN girebilsin).
    private void ApplyNewDefaults(MetalGetDto m)
    {
        m.IsActive = true;
        m.Factor = 0.995m;

        // Panel tohumu: kod/ad üründen türetilip geliyorsa boş form yerine hazır gelir (U1).
        if (!string.IsNullOrWhiteSpace(SeedCode))
        {
            m.Code = SeedCode!;
        }

        if (!string.IsNullOrWhiteSpace(SeedName))
        {
            m.Name = SeedName!;
        }

        // Nitelik×değer üretilince (GenerateVariants) liste değişir; üretilmezse save'de synchronizer bu main'i kalıcılaştırır.
        m.Variants.Add(new MetalVariantGraphDto
        {
            IsMain = true,
            Code = EntityVariantConsts.MainVariantCode,
            Name = EntityVariantConsts.MainVariantName,
            IsActive = true,
        });
    }

    // OTOMATİK varyant senkronu — nitelik/değer add/edit/delete'te çağrılır (EntityAttributesPanel.OnAttributesChanged;
    // "Oluştur" butonuna bağlı DEĞİL). MERGE: var olan kombinasyonların düzenlemeleri (barkod/GTIN) KORUNUR. Değersiz
    // nitelik varken (kullanıcı hâlâ değer ekliyor) SESSİZCE atlar (transient; toast/veri değişikliği yok).
    private async Task GenerateVariantsAsync(MetalGetDto model)
    {
        if (VariantGraphMerge.HasIncompleteAttribute(model.Attributes))
        {
            return;
        }

        try
        {
            var generated = await MetalAppService.GenerateVariantsAsync(new EntityVariantGenerateRequestDto
            {
                OwnerName = model.Name,
                Attributes = model.Attributes,
            });
            var generatedMetals = generated.Select(g => new MetalVariantGraphDto
            {
                Id = g.Id,
                Code = g.Code,
                Name = g.Name,
                IsMain = g.IsMain,
                IsActive = g.IsActive,
                CombinationKey = g.CombinationKey,
                AttributeSummary = g.AttributeSummary,
                ClientKey = g.ClientKey
            }).ToList();

            VariantGraphMerge.Apply(model.Variants, generatedMetals);
        }
        catch (BusinessException bex)
        {
            // In-process BusinessException lokalize OLMAZ (Blazor Server) → kodu resource'tan çevir.
            UiService.ShowErrorToast(L[bex.Code ?? bex.Message].Value);
        }
    }
}


