using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Geography;

/// <summary>
/// core coğrafya OKUMA servisi + lazy import tetiği. Coğrafya entity'leri host-global (IMultiTenant DEĞİL)
/// → okumalar doğrudan DB'den, filtre kapatmaya gerek yok. Ülke satırı hâlâ host‖tenant (import-flag kontrolünde
/// <c>IMultiTenant</c> kapatılır). Ülkenin
/// il/eyalet listesi ilk kez istendiğinde <see cref="Country.GeographyImportedAt"/> boşsa ÖNCE
/// <see cref="GeographyImportManager.ImportCountryAsync"/> koşar (idempotent; TR guard manager'da), sonra liste döner.
/// Yetki: Countries kataloğunun mevcut policy'si (yeni permission bilinçli açılmadı — coğrafya ülke kataloğunun eki).
/// </summary>
[Authorize(TradeXpressPermissions.Countries.Default)]
public class GeographyAppService : ApplicationService, IGeographyAppService
{
    private readonly IRepository<Country, Guid> _countryRepository;
    private readonly IRepository<AdministrativeArea, Guid> _administrativeAreaRepository;
    private readonly IRepository<Locality, Guid> _localityRepository;
    private readonly IRepository<N11District, Guid> _n11DistrictRepository;
    private readonly IN11CityAppService _n11CityAppService;
    private readonly GeographyImportManager _importManager;

    public GeographyAppService(
        IRepository<Country, Guid> countryRepository,
        IRepository<AdministrativeArea, Guid> administrativeAreaRepository,
        IRepository<Locality, Guid> localityRepository,
        IRepository<N11District, Guid> n11DistrictRepository,
        IN11CityAppService n11CityAppService,
        GeographyImportManager importManager)
    {
        _countryRepository = countryRepository;
        _administrativeAreaRepository = administrativeAreaRepository;
        _localityRepository = localityRepository;
        _n11DistrictRepository = n11DistrictRepository;
        _n11CityAppService = n11CityAppService;
        _importManager = importManager;
        LocalizationResource = typeof(TradeXpressResource);
    }

    public virtual async Task<ListResultDto<AdministrativeAreaDto>> GetAdministrativeAreasAsync(Guid countryId)
    {
        if (await NeedsAreaImportAsync(countryId))
        {
            // Lazy tetik (üst katman): ilk ihtiyaçta yalnız il/EYALET verisini dataset'ten çek (şehir DEĞİL — şehirler
            // eyalet seçilince per-state iner). Manager kendi UoW'larını yönetir; idempotent + TR guard.
            await _importManager.ImportCountryAreasAsync(countryId);
        }

        // Coğrafya host-global (IMultiTenant değil) → tenant filtresi yok; doğrudan DB'den oku.
        var entities = await AsyncExecuter.ToListAsync(
            (await _administrativeAreaRepository.GetQueryableAsync())
                .Where(a => a.CountryId == countryId)
                .OrderBy(a => a.Name));

        return new ListResultDto<AdministrativeAreaDto>(
            entities.Select(a => ObjectMapper.Map<AdministrativeArea, AdministrativeAreaDto>(a)).ToList());
    }

    public virtual async Task<ListResultDto<LocalityDto>> GetLocalitiesAsync(Guid administrativeAreaId)
    {
        if (await AreaNeedsLocalityImportAsync(administrativeAreaId))
        {
            // Lazy tetik (alt katman): bu eyaletin şehirleri henüz inmemişse İLK seçimde dataset'ten per-state çek
            // (US için 19k değil ~300 şehir; birkaç sn — picker busy hint gösterir). Manager idempotent + TR guard.
            await _importManager.ImportAreaLocalitiesAsync(administrativeAreaId);
        }

        // Coğrafya host-global (IMultiTenant değil) → tenant filtresi yok; import sonrası doğrudan DB'den oku.
        var entities = await AsyncExecuter.ToListAsync(
            (await _localityRepository.GetQueryableAsync())
                .Where(l => l.AdministrativeAreaId == administrativeAreaId)
                .OrderBy(l => l.Name));

        return new ListResultDto<LocalityDto>(
            entities.Select(l => ObjectMapper.Map<Locality, LocalityDto>(l)).ToList());
    }

    public virtual async Task<List<NeighborhoodDto>> GetNeighborhoodsAsync(Guid localityId)
    {
        // Mahalle il/ilçe'nin AKSİNE SAKLANMAZ (bilinçli istisna: hacim + N11 zaten canlı veriyor) → her çağrıda
        // N11'den taze çekilir, DB'ye YAZILMAZ. Yerelliğin N11 ilçe id'si N11District.CoreLocalityId'den çözülür.
        var districtId = await ResolveN11DistrictIdAsync(localityId);
        if (districtId == null)
        {
            // TR-dışı / N11-linksiz yerellik → mahalle kaynağı yok: boş liste (picker mahalle katmanını boş gösterir).
            return new List<NeighborhoodDto>();
        }

        try
        {
            // MEVCUT canlı fetch (host kimliği N11 servisinde çözülür; saklamaz) — sonucu Geography DTO'suna projekte et.
            var neighborhoods = await _n11CityAppService.GetNeighborhoodsAsync(districtId);
            return neighborhoods
                .Select(n => new NeighborhoodDto { Id = n.Id, Name = n.Name })
                .ToList();
        }
        catch (BusinessException)
        {
            throw; // zaten lokalize/kodlu (ör. N11 kimliği eksik) — olduğu gibi yükselt
        }
        catch (Exception ex)
        {
            // N11 erişilemez → dostane lokalize hata (picker toast'a çevirir).
            throw new BusinessException("TradeXpress:Geography:NeighborhoodsUnavailable", innerException: ex);
        }
    }

    // core yerelliğin N11 ilçe id'sini id-only kolondan çözer (N11District.CoreLocalityId == localityId). TR ilçeleri
    // N11'den türedi → CoreLocalityId GeographySeeder'da dolduruldu. Eşleşme yoksa null (TR-dışı/linksiz → mahalle kaynağı yok).
    // N11District host-global (IMultiTenant DEĞİL) → tenant filtresi yok; doğrudan projeksiyon sorgusu.
    private async Task<string?> ResolveN11DistrictIdAsync(Guid localityId)
    {
        return await AsyncExecuter.FirstOrDefaultAsync(
            (await _n11DistrictRepository.GetQueryableAsync())
                .Where(d => d.CoreLocalityId == localityId)
                .Select(d => d.DistrictId));
    }

    // Ülkeyi host‖tenant görünürlüğünde projeksiyonla yoklar (entity izlenmez); yoksa dostane not-found.
    // İdari alan (üst katman) importu gerekli mi: GeographyImportedAt null ise.
    private async Task<bool> NeedsAreaImportAsync(Guid countryId)
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            var flags = await AsyncExecuter.FirstOrDefaultAsync(
                (await _countryRepository.GetQueryableAsync())
                    .Where(c => c.Id == countryId)
                    .Select(c => new { c.Id, c.GeographyImportedAt }));
            if (flags == null)
            {
                throw new EntityNotFoundException(typeof(Country), countryId);
            }

            return flags.GeographyImportedAt == null;
        }
    }

    // İdari alanın yerellik (alt katman) importu gerekli mi: LocalitiesImportedAt null ise. İdari alan host-global
    // (IMultiTenant DEĞİL) → filtre kapatmaya gerek yok; projeksiyonla yoklanır (entity izlenmez).
    private async Task<bool> AreaNeedsLocalityImportAsync(Guid administrativeAreaId)
    {
        var flags = await AsyncExecuter.FirstOrDefaultAsync(
            (await _administrativeAreaRepository.GetQueryableAsync())
                .Where(a => a.Id == administrativeAreaId)
                .Select(a => new { a.Id, a.LocalitiesImportedAt }));
        if (flags == null)
        {
            throw new EntityNotFoundException(typeof(AdministrativeArea), administrativeAreaId);
        }

        return flags.LocalitiesImportedAt == null;
    }
}
