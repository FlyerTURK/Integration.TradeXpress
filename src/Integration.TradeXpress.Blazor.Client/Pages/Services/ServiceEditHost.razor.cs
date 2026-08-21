using System;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Services;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Services;

/// <summary>Hizmet edit host — ince host (coordinator + config; global Service tenant'ta salt-okunur).
/// (@code bloğu 2026-08-07'de code-behind'a taşındı — dosyaya dokunma kuralı.)</summary>
public partial class ServiceEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    /// <summary>ÇAĞRI-BAŞI footer daraltma (2026-08-06 Hakan kararı) — gerekçe GoodEditHost'ta.</summary>
    [Parameter] public bool SupportsSaveAndNew { get; set; } = true;

    [Parameter] public bool SupportsDelete { get; set; } = true;

    /// <summary>Sınıflandırma panelinden ÖN-DOLDURMA (2026-08-07 U1 — gerekçe MetalEditHost'ta). Seed yazımı
    /// razor'daki <c>ApplyNewDefaults</c> lambda'sında.</summary>
    [Parameter] public string? SeedCode { get; set; }

    [Parameter] public string? SeedName { get; set; }

    /// <summary>ÜRÜNÜN HİZMET PROJEKSİYONU — <c>ProductToCommodityProjector</c> çıktısı (2026-08-20). Hizmet
    /// stoklanan emtia değil reçeteye giren ÜCRET kalemidir; stoklanmayanın varyantı da görseli de olmaz →
    /// seed yalnız KİMLİK taşır.
    /// <para>Verilirse <see cref="SeedCode"/>/<see cref="SeedName"/>'i EZER.</para></summary>
    [Parameter] public ServiceGetDto? SeedModel { get; set; }

    private ICommitCoordinator<ServiceGetDto, ServiceListDto, Guid, ServiceListRequestDto>? _coordinator;

    /// <summary>Kurulum SENKRON: burada await edilecek hiçbir şey yok (hizmet formu lookup/working context
    /// istemeyen tek emtia formu) → kardeş host'lardaki <c>_ready</c> hazırlık bayrağı burada GEREKMEZ.
    /// Gerekçenin tamamı ServiceEditHost.razor başındaki yorumda; <c>_ready</c>, bu metoda bir await girdiği gün eklenir.</summary>
    protected override void OnInitialized()
    {
        _coordinator = new PersistentCoordinator<ServiceGetDto, ServiceListDto, Guid, ServiceListRequestDto, ServiceCreateDto, ServiceUpdateDto>(
            ServiceAppService, Mapper);
    }

    private void ApplyNewDefaults(ServiceGetDto m)
    {
        m.IsActive = true;

        // ZENGİN SEED önce (gerekçe MetalEditHost'ta) — hizmette "zengin" = kimlik + açıklama.
        if (SeedModel is { } s)
        {
            m.Code        = s.Code;
            m.Name        = s.Name;
            m.Description = s.Description;
            return;
        }

        // Panel seed'i (U1 — gerekçe MetalEditHost'ta).
        if (!string.IsNullOrWhiteSpace(SeedCode))
        {
            m.Code = SeedCode!;
        }

        if (!string.IsNullOrWhiteSpace(SeedName))
        {
            m.Name = SeedName!;
        }
    }
}
