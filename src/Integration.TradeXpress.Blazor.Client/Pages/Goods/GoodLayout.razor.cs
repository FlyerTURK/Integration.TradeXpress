using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Goods;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Goods;

/// <summary>Mamül perakende stok kartı (dumb Layout) — GoodGetDto'ya bind eder; servis çağırmaz (lookup
/// verisini host yükleyip geçer). Ana tedarikçi cascade + türetilmiş satış önizlemesi + tedarikçi
/// drill yardımcıları burada.</summary>
public partial class GoodLayout
{
    [Parameter, EditorRequired] public GoodGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }
    [Parameter] public IReadOnlyList<CurrencyUnitListDto> CurrencyUnits { get; set; } = Array.Empty<CurrencyUnitListDto>();
    [Parameter] public IReadOnlyList<AccountListDto> Accounts { get; set; } = Array.Empty<AccountListDto>();
    [Parameter] public IReadOnlyList<SubAccountListDto> SubAccounts { get; set; } = Array.Empty<SubAccountListDto>();

    /// <summary>Working şirket ülke parası — yeni tedarikçi satırı temin fiyatı birimi default'u (boşsa).</summary>
    [Parameter] public Guid? LocalCurrencyUnitId { get; set; }

    // Lookup ekle/düzelt sonrası host listeyi tazeler (EditComponentType + EntityChange → OnLookupReloadRequested).
    [Parameter] public EventCallback OnReloadAccounts { get; set; }
    [Parameter] public EventCallback OnReloadSubAccounts { get; set; }
    [Parameter] public EventCallback OnReloadCurrencyUnits { get; set; }

    // Drill değişimini forma bildir (dirty/Save) — EntityEditForm EditChanged cascade'i.
    [CascadingParameter(Name = "EditChanged")] private Action? EditChanged { get; set; }

    private DrillList<GoodSupplierDto>? _supplierDrill;

    // KDV hazır oranları (TR: %1/%10/%20) — combo'da quick-pick; liste-dışı serbest oran da yazılabilir.
    private static readonly decimal[] VatPresets = { 1m, 10m, 20m };

    /// <summary>"Varyantları Oluştur" — layout DUMB (servis çağırmaz): işi host yapar (GoodAppService.GenerateVariantsAsync
    /// → Model.Variants). Jenerik EntityVariantsPanel'e geçilir.</summary>
    [Parameter] public EventCallback OnGenerateVariants { get; set; }

    // Fiyat/stok-limiti ana mamülde DEĞİL → varyant-başı (aşağıdaki ExitPreviewOf varyant için).

    // Varyant-başı türetilmiş satış önizlemesi — MarginSetting.Apply ile aynı hesap (sunucu OTORİTER; canlı UI geri bildirimi).
    private static decimal ExitPreviewOf(GoodVariantGraphDto v)
    {
        return v.MarginType switch
        {
            MarginType.FinalPrice => v.MarginValue,
            MarginType.Multiply => v.EntryPrice * v.MarginValue,
            MarginType.Amount => v.EntryPrice + v.MarginValue,
            MarginType.Percent => v.EntryPrice * (1m + v.MarginValue / 100m),
            _ => v.EntryPrice,
        };
    }

    /// <summary>Tedarikçi kaydetme engeli: CARİ HESAP seçilmemişse satır kabul edilmez (alt hesap opsiyonel). Aksi halde
    /// sunucu SaveGraph'ta AccountId=Empty satırı sessizce eler (veri sessizce kaybolmasın).</summary>
    private string? SupplierSaveGuard(GoodSupplierDto candidate)
    {
        return candidate.AccountId == Guid.Empty ? L["TradeXpress:GoodSupplier:AccountRequired"].Value : null;
    }
}
