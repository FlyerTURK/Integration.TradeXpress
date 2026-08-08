using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Trendyol;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.TrendyolBrands;

/// <summary>
/// <see cref="ITrendyolBrandAppService"/> — marka type-ahead + HYBRID cache okuma (K3). Kimlik, çalışılan şirketin
/// Trendyol kanalından çözülür (per-kanal; merkezi tek kimlik YOK) ve aramayı istemciye delege eder. Arama CANLI
/// kalır (pazaryerine SIFIR yazma); picker açılış beslemesi ise host-global <see cref="TrendyolBrand"/> write-through
/// cache'inden okunur (<see cref="GetCachedListAsync"/>). TrendyolCategoryAppService ile hizalı — ayrı izin yok.
///
/// <para><b>Yetki (2026-08-07 G1):</b> tek tüketici Trendyol ürün formu (kanal ekranı) ve arama CANLI Trendyol
/// API'sine gider (dış kota) → kanal ailesiyle aynı sınır. Öncesinde sınıf ANONİMDİ — dış API kotası kimliksiz
/// tüketilebilirdi.</para>
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class TrendyolBrandAppService : TradeXpressAppService, ITrendyolBrandAppService
{
    private readonly ITrendyolBrandClient _client;
    private readonly ITrendyolCredentialResolver _credentialResolver;
    private readonly IRepository<TrendyolBrand, Guid> _brandRepository;

    public TrendyolBrandAppService(
        ITrendyolBrandClient client,
        ITrendyolCredentialResolver credentialResolver,
        IRepository<TrendyolBrand, Guid> brandRepository)
    {
        _client = client;
        _credentialResolver = credentialResolver;
        _brandRepository = brandRepository;
    }

    public virtual async Task<List<TrendyolBrandDto>> SearchAsync(string term)
    {
        // Type-ahead: en az 2 harf; aksi halde API'ye gitme (tek-harf gürültüsü + istemciye liste dökme yok).
        var trimmed = term?.Trim() ?? string.Empty;
        if (trimmed.Length < 2)
        {
            return new List<TrendyolBrandDto>();
        }

        var credentials = await _credentialResolver.ResolveForCurrentCompanyAsync();
        return await _client.SearchByNameAsync(credentials, trimmed);
    }

    public virtual async Task<List<TrendyolBrandDto>> GetCachedListAsync()
    {
        // Host-global okuma → host'a sabitle (N11ShipmentCompany deseni; db-per-tenant'a karşı merkezilik garantisi).
        using (CurrentTenant.Change(null))
        {
            var items = await AsyncExecuter.ToListAsync(
                (await _brandRepository.GetQueryableAsync()).OrderBy(x => x.Name));
            return items.Select(x => ObjectMapper.Map<TrendyolBrand, TrendyolBrandDto>(x)).ToList();
        }
    }
}
