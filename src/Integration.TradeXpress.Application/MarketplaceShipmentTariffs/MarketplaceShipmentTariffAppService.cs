using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>
/// Kargo tarifesi okuma servisi. Tarife HOST-GLOBAL olduğundan okuma host bağlamına sabitlenir — aksi hâlde
/// tenant bağlamındaki bir sorgu (ileride db-per-tenant'a geçilirse) merkezi kataloğu göremezdi.
/// <para>Yazma ucu YOK: veri gömülü yayın dosyasından seed edilir (bkz. <see cref="MarketplaceShipmentTariffSeeder"/>).</para>
/// <para><b>Yetki (2026-08-07 G1):</b> policy'siz <c>[Authorize]</c> — salt-okuma referans verisi ve tüketici
/// kısıtı yok (kanal-dışı yüzeylerden de okunabilir); kimlik yeter. Öncesinde ANONİMDİ (konvansiyon ağı kırmızı).</para>
/// </summary>
[Authorize]
public class MarketplaceShipmentTariffAppService(
    IRepository<MarketplaceShipmentTariff, Guid> tariffRepository)
    : TradeXpressAppService, IMarketplaceShipmentTariffAppService
{
    private readonly IRepository<MarketplaceShipmentTariff, Guid> _tariffRepository = tariffRepository;

    public virtual async Task<List<MarketplaceShipmentTariffDto>> GetListAsync(
        MarketplaceShipmentTariffListInput input)
    {
        Check.NotNull(input, nameof(input));

        // Host-global okuma → host'a sabitle (merkezilik garantisi; N11CityAppService ile aynı gerekçe).
        using (CurrentTenant.Change(null))
        {
            var query = await _tariffRepository.WithDetailsAsync(t => t.Rates);

            if (input.Channel is { } channel)
            {
                query = query.Where(t => t.Channel == channel);
            }

            if (input.OnlyEffective)
            {
                query = query.Where(t => t.EffectiveTo == null && t.IsActive);
            }

            var tariffs = query
                .OrderBy(t => t.Channel)
                .ThenBy(t => t.CarrierName)
                .ThenByDescending(t => t.EffectiveFrom)
                .ToList();

            return tariffs
                .Select(ObjectMapper.Map<MarketplaceShipmentTariff, MarketplaceShipmentTariffDto>)
                .ToList();
        }
    }

    public virtual async Task<MarketplaceShipmentTariffDetailDto> GetAsync(Guid id)
    {
        using (CurrentTenant.Change(null))
        {
            var query = await _tariffRepository.WithDetailsAsync(t => t.Rates);
            var tariff = query.FirstOrDefault(t => t.Id == id)
                ?? throw new BusinessException("TradeXpress:ShipmentTariff:NotFound").WithData("id", id);

            var dto = ObjectMapper.Map<MarketplaceShipmentTariff, MarketplaceShipmentTariffDetailDto>(tariff);

            // Sıralama görüntü kuralıdır (eşleme değil): desi artan, barem sepet alt sınırına göre.
            dto.Rates = dto.Rates.OrderBy(r => r.Desi).ToList();
            dto.ConditionalRates = dto.ConditionalRates.OrderBy(r => r.BasketFrom).ToList();

            return dto;
        }
    }

}
