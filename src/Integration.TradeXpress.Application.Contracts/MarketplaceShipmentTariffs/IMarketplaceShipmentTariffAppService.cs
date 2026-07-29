using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>
/// Pazaryeri anlaşmalı kargo tarifesi okuma servisi.
/// <para><b>Salt okunur:</b> tarife pazaryerinin yayımladığı listedir, kullanıcı düzenlemez — kaynağı gömülü
/// yayın dosyasıdır ve seed ile kurulur. Şirketin kendi anlaşması varsa kanalın "elle girilen kargo tutarı"
/// alanından verilir ve o daima kazanır (2026-07-26 Hakan kararı).</para>
/// </summary>
public interface IMarketplaceShipmentTariffAppService : IApplicationService
{
    /// <summary>Tarife başlıklarını listeler (desi tablosu HARİÇ — 101 satır × N taşıyıcı taşınmasın).</summary>
    Task<List<MarketplaceShipmentTariffDto>> GetListAsync(MarketplaceShipmentTariffListInput input);

    /// <summary>Tek tarifenin tam görünümü — desi tablosu + şartlı barem dahil.</summary>
    Task<MarketplaceShipmentTariffDetailDto> GetAsync(Guid id);
}
