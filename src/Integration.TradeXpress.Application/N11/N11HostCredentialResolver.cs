using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.N11;

/// <summary>
/// N11 host kimliği çözücü — host-global N11 referans çağrılarının (mahalle canlı çekimi, il/ilçe sync) kredensiyel
/// kaynağı. Kademeli çözüm:
/// <list type="number">
///   <item>Host config <c>N11:CategorySync:AppKey/AppSecret</c> — atanmış host hesabı (varsa öncelik).</item>
///   <item>Mevcut scope'un aktif N11 kanalı — filtreler AÇIK (çalışılan tenant/şirkete daralır; sızıntı yok).</item>
///   <item>Müsait HERHANGİ bir aktif N11 kanalı — tenant + şirket filtreleri KASITLI kapatılır (son çare).</item>
/// </list>
/// Mahalle/şehir HOST-GLOBAL kamu referansı olduğundan hangi hesabın çektiği veriyi değiştirmez; kredensiyel
/// yalnız SERVER-SIDE kullanılır, istemciye TAŞINMAZ/loglanmaz. 3. kademedeki cross-tenant kullanım bilinçli ve
/// kullanıcı onaylıdır (2026-07-21: "mahalle N11'den gelsin, müsait olan N11 hesabını kullan"). Soft-delete filtresi
/// AÇIK kalır (silinmiş kanal hesabı seçilmez).
/// </summary>
public class N11HostCredentialResolver : IN11HostCredentialResolver, ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IDataFilter _dataFilter;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public N11HostCredentialResolver(
        IConfiguration configuration,
        IRepository<SalesChannelTrN11, Guid> channelRepository,
        IDataFilter dataFilter,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _configuration = configuration;
        _channelRepository = channelRepository;
        _dataFilter = dataFilter;
        _asyncExecuter = asyncExecuter;
    }

    public virtual async Task<(string AppKey, string AppSecret)> ResolveAsync()
    {
        // Tier 1 — atanmış host hesabı (config): ikisi de doluysa öncelik.
        var appKey = _configuration["N11:CategorySync:AppKey"];
        var appSecret = _configuration["N11:CategorySync:AppSecret"];
        if (!string.IsNullOrWhiteSpace(appKey) && !string.IsNullOrWhiteSpace(appSecret))
        {
            return (appKey!, appSecret!);
        }

        // Tier 2 — mevcut scope'un (çalışılan tenant/şirket) aktif N11 kanalı; filtreler AÇIK → sızıntı yok.
        var scoped = await FindFirstActiveChannelAsync();
        if (scoped is not null)
        {
            return (scoped.AppKey, scoped.AppSecret);
        }

        // Tier 3 — müsait HERHANGİ bir aktif N11 kanalı. Kamu referans verisi + server-side kredensiyel → tenant +
        // şirket filtreleri kasıtla kapatılır (soft-delete AÇIK kalır: silinmiş kanal hariç). Kullanıcı onaylı son çare.
        using (_dataFilter.Disable<IMultiTenant>())
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            var any = await FindFirstActiveChannelAsync();
            if (any is not null)
            {
                return (any.AppKey, any.AppSecret);
            }
        }

        throw new BusinessException("TradeXpress:N11:NoCredentialsAvailable");
    }

    // İlk aktif N11 kanalı (deterministik: en eski). AppKey/AppSecret entity guard'ıyla daima doludur.
    private async Task<SalesChannelTrN11?> FindFirstActiveChannelAsync()
    {
        return await _asyncExecuter.FirstOrDefaultAsync(
            (await _channelRepository.GetQueryableAsync())
                .Where(c => c.IsActive)
                .OrderBy(c => c.CreationTime));
    }
}
