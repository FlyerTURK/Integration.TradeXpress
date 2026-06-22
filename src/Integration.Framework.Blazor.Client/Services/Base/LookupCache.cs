using Integration.Framework.Blazor.Client.Services.Mdi;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// Bir referans entity'sinin lookup listesi için read-koordinatör (ERPPROV3 deseni).
/// Edit form'lar her açılışta API'ye gitmesin diye listeyi TTL'li bellekte tutar; ilgili entity
/// değişince (Create/Update/Delete) <see cref="IEntityChangeNotifier"/> üzerinden kendini geçersiz kılar.
/// Blazor Server'da <b>Scoped</b> (devre başına) kaydedilir → tenant izolasyonu + 5dk TTL yeterli guard.
/// </summary>
public interface ILookupCache<TListDto> where TListDto : class
{
    /// <summary>Taze ise bellekten, değilse API'den çekip cache'leyerek lookup listesini döndürür (asla null değil).</summary>
    Task<IReadOnlyList<TListDto>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Cache'i elle boşaltır (notifier aboneliği zaten otomatik yapar; manuel/test için açık).</summary>
    void Invalidate();
}

/// <summary>
/// Generic <see cref="ILookupCache{TListDto}"/> uygulaması. Per-entity DI kaydında fetch delegesi +
/// entity anahtarı verilir; entity-spesifik kod yazılmaz (1000 entity için tek satır kayıt).
/// <para><c>entityKey</c>, <c>CrudEditHost</c>'un notify ettiği anahtarla (varsayılan
/// <c>typeof(TListDto).FullName</c>) AYNI olmalı — yoksa auto-invalidate tetiklenmez.</para>
/// </summary>
public sealed class LookupCache<TListDto> : ILookupCache<TListDto>, IDisposable
    where TListDto : class
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly Func<CancellationToken, Task<List<TListDto>>> _fetch;
    private readonly IEntityChangeNotifier _notifier;
    private readonly string _entityKey;

    private List<TListDto>? _cache;
    private DateTime _fetchedUtc;

    public LookupCache(
        Func<CancellationToken, Task<List<TListDto>>> fetch,
        IEntityChangeNotifier notifier,
        string entityKey)
    {
        _fetch = fetch;
        _notifier = notifier;
        _entityKey = entityKey;
        _notifier.EntityChanged += OnEntityChanged;
    }

    public async Task<IReadOnlyList<TListDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is not null && DateTime.UtcNow - _fetchedUtc < Ttl)
            return _cache;

        // Hata yutulmaz: çağıran exception'ı görür; cache "poisoned null" ile dolmaz.
        _cache = await _fetch(cancellationToken);
        _fetchedUtc = DateTime.UtcNow;
        return _cache;
    }

    public void Invalidate() => _cache = null;

    // Bu entity (kendi anahtarı) değişince cache'i düşür → sonraki GetAsync taze çeker.
    private void OnEntityChanged(string key)
    {
        if (key == _entityKey)
            _cache = null;
    }

    public void Dispose() => _notifier.EntityChanged -= OnEntityChanged;
}
