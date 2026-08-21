using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.TrendyolShipments;

/// <summary>
/// Yeni Trendyol kanalına konacak VARSAYILAN kargo firmasını seçer — tek karar yeri.
///
/// <para><b>Neden ayrı sınıf:</b> aynı cevabı üç çağıran soruyor (kanal oluşturma · kanal edit formunun ilk
/// açılışı · kurulum sihirbazı). Üçü kendi başına seçseydi, kullanıcının sihirbazda gördüğü firma ile kaydın
/// aldığı firma farklı olabilirdi — ve bu fark ancak ilk gönderimde görülürdü.</para>
///
/// <para><b>Neden Trendyol Express:</b> platformun KENDİ kuryesi ve satıcı panelinde de öntanımlı olan
/// firma. Seçim bir BAŞLANGIÇ değeridir, dayatma değil — kullanıcı formdan değiştirir ve değiştirdiği an
/// bu çözücü bir daha karışmaz (yalnız <c>null</c> iken devreye girer).</para>
///
/// <para><b>Zincirin sonu BOŞTUR, uydurma DEĞİL:</b> Trendyol Express pasifleştirilmişse ilk aktif firmaya
/// düşülür; hiç firma yoksa <c>null</c> döner ve kanal kargosuz kaydedilir. Rastgele bir firma yazmak,
/// kullanıcının hiç görmediği bir kararı sessizce onun adına vermek olurdu.</para>
/// </summary>
public class TrendyolDefaultCargoProviderResolver : ITransientDependency
{
    /// <summary>Trendyol Express Marketplace — platformun kendi kuryesi (resmî listede <c>17</c>).</summary>
    public const string PreferredExternalId = "17";

    private readonly IRepository<TrendyolCargoProvider, Guid> _repository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public TrendyolDefaultCargoProviderResolver(
        IRepository<TrendyolCargoProvider, Guid> repository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _repository = repository;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>Varsayılan firmanın kimliği; hiç aktif firma yoksa <c>null</c>.</summary>
    public virtual async Task<Guid?> ResolveAsync()
    {
        var query = (await _repository.GetQueryableAsync()).Where(p => p.IsActive);
        var active = await _asyncExecuter.ToListAsync(query);
        if (active.Count == 0)
        {
            return null;
        }

        var preferred = active.FirstOrDefault(p => string.Equals(p.ExternalId, PreferredExternalId, StringComparison.Ordinal));
        if (preferred is not null)
        {
            return preferred.Id;
        }

        // Yedek: sayısal id'ye göre en küçüğü — sıralamasız FirstOrDefault her çağrıda başka firma
        // döndürebilirdi ve "varsayılan" her açılışta değişen bir şey OLAMAZ.
        var fallback = active
            .OrderBy(p => int.TryParse(p.ExternalId, out var numeric) ? numeric : int.MaxValue)
            .ThenBy(p => p.ExternalId, StringComparer.Ordinal)
            .First();

        return fallback.Id;
    }
}
