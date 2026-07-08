using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Trendyol;

namespace Integration.TradeXpress.TrendyolBrands;

/// <summary>
/// <see cref="ITrendyolBrandAppService"/> — marka type-ahead. Kimlik, çalışılan şirketin Trendyol kanalından çözülür
/// (per-kanal; merkezi tek kimlik YOK) ve aramayı istemciye delege eder. Marka verisi UÇUCU (entity/DB yok);
/// yalnız GET (pazaryerine SIFIR yazma). TrendyolCategoryAppService ile hizalı — ayrı izin yok.
/// </summary>
public class TrendyolBrandAppService : TradeXpressAppService, ITrendyolBrandAppService
{
    private readonly ITrendyolBrandClient _client;
    private readonly ITrendyolCredentialResolver _credentialResolver;

    public TrendyolBrandAppService(
        ITrendyolBrandClient client,
        ITrendyolCredentialResolver credentialResolver)
    {
        _client = client;
        _credentialResolver = credentialResolver;
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
}
