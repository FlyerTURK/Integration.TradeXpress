using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Geography;

/// <summary>
/// Çekirdek coğrafya okuma servisi — UI HEP DB'den okur. Bir ülkenin il/eyalet listesi İLK istendiğinde veri
/// DB'de yoksa on-demand import tetiklenir (dr5hn dataset'i → DB); sonraki istekler doğrudan DB'den döner.
/// Felsefe: kullanılmayan ülkelerin şehirleri veritabanına İNMEZ ("gereksiz yere Uganda'nın şehirleri olmasın").
/// </summary>
public interface IGeographyAppService : IApplicationService
{
    /// <summary>Ülkenin idari alanlarını (il/eyalet) döner; veri yoksa ÖNCE on-demand import çalışır (lazy tetik).</summary>
    Task<ListResultDto<AdministrativeAreaDto>> GetAdministrativeAreasAsync(Guid countryId);

    /// <summary>İdari alanın yerelliklerini (ilçe/şehir) döner — import ülke seviyesinde zaten yapıldığından DB'den okur.</summary>
    Task<ListResultDto<LocalityDto>> GetLocalitiesAsync(Guid administrativeAreaId);

    /// <summary>Yerelliğin (ilçe) mahallelerini CANLI N11'den çeker (SAKLANMAZ — her çağrıda taze; il/ilçe'nin aksine
    /// bilinçli persistence-siz istisna). Yerelliğin N11 ilçe id'si köprüden (<c>N11District.CoreLocalityId</c>) çözülür;
    /// TR-dışı/N11-linksiz yerellikte BOŞ liste döner.</summary>
    Task<List<NeighborhoodDto>> GetNeighborhoodsAsync(Guid localityId);
}
