using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.SalesChannels.Etsy;

/// <summary>
/// <see cref="IEtsyTokenProvider"/> implementasyonu. Eşzamanlı yenileme koruması: kanal-başına
/// <see cref="SemaphoreSlim"/> (process-içi — Blazor Server tek host, dağıtık kilit YAGNI). Kilit içinde taze
/// okuma + ikinci kontrol: bekleyen çağrı, önceki çağrının yenilediği token'ı görür ve İKİNCİ refresh yapmaz
/// (rotasyonda eski refresh token tek kullanımlık olabilir — çift refresh bağlantıyı düşürürdü).
/// </summary>
public class EtsyTokenProvider : IEtsyTokenProvider, ITransientDependency
{
    // Kanal-başına refresh kilidi — servis transient ama kilit process-geneli olmalı → static.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> RefreshLocks = new();

    private readonly IRepository<SalesChannelEtsy, Guid> _repository;
    private readonly IEtsyOAuthClient _oauthClient;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IClock _clock;

    public EtsyTokenProvider(
        IRepository<SalesChannelEtsy, Guid> repository,
        IEtsyOAuthClient oauthClient,
        IUnitOfWorkManager unitOfWorkManager,
        IClock clock)
    {
        _repository = repository;
        _oauthClient = oauthClient;
        _unitOfWorkManager = unitOfWorkManager;
        _clock = clock;
    }

    public virtual async Task<string> GetAccessTokenAsync(Guid channelId, CancellationToken cancellationToken = default)
    {
        // Hızlı yol: token hâlâ geçerliyse kilitsiz dön (tipik durum — refresh saatte bir gerekir).
        var channel = await _repository.GetAsync(channelId, cancellationToken: cancellationToken);
        if (HasUsableAccessToken(channel))
        {
            return channel.AccessToken!;
        }

        EnsureRefreshable(channel);

        var refreshLock = RefreshLocks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Kilit içinde TAZE oku (requiresNew UoW → EF cache'i değil DB'deki güncel satır): önceki bekleyen
            // çağrı yenilemişse ikinci refresh YAPMA (rotasyonlu refresh token'ı boşa harcama).
            using var uow = _unitOfWorkManager.Begin(requiresNew: true);
            var fresh = await _repository.GetAsync(channelId, cancellationToken: cancellationToken);
            if (HasUsableAccessToken(fresh))
            {
                return fresh.AccessToken!;
            }

            EnsureRefreshable(fresh);

            var tokens = await _oauthClient.RefreshAsync(fresh.Keystring, fresh.RefreshToken!, cancellationToken);

            // Rotasyon: dönen YENİ refresh token'ı hemen persist et (eskisi geçersizleşmiş olabilir).
            var utcNow = _clock.Now.ToUniversalTime();
            fresh.SetTokens(
                tokens.AccessToken,
                utcNow.AddSeconds(tokens.ExpiresInSeconds),
                tokens.RefreshToken,
                utcNow.AddDays(EtsyOAuthConsts.RefreshTokenLifetimeDays));

            await _repository.UpdateAsync(fresh, autoSave: true, cancellationToken: cancellationToken);
            await uow.CompleteAsync();

            return tokens.AccessToken;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    /// <summary>Access token dolu ve süresine (pay dahil) en az <see cref="EtsyOAuthConsts.AccessTokenExpirySkewSeconds"/>
    /// sn var mı — varsa refresh gereksiz.</summary>
    private bool HasUsableAccessToken(SalesChannelEtsy channel)
    {
        return !string.IsNullOrEmpty(channel.AccessToken)
            && channel.AccessTokenExpiresAt.HasValue
            && channel.AccessTokenExpiresAt.Value
                > _clock.Now.ToUniversalTime().AddSeconds(EtsyOAuthConsts.AccessTokenExpirySkewSeconds);
    }

    /// <summary>Refresh mümkün mü: kanal bağlı (refresh token dolu + süresi geçmemiş) değilse dostane
    /// "yeniden bağlan" hatası (90 gün pasiflikte Etsy bağlantıyı düşürür).</summary>
    private void EnsureRefreshable(SalesChannelEtsy channel)
    {
        if (!channel.IsConnected(_clock.Now.ToUniversalTime()))
        {
            throw new BusinessException("TradeXpress:SalesChannel:Etsy:NotConnected");
        }
    }
}
