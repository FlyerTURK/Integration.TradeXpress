using Microsoft.Extensions.Logging;

namespace Integration.TradeXpress.TrendyolBrands;

/// <summary>
/// Trendyol marka write-through cache yazıcısı — kullanıcının kanal-üründe SEÇTİĞİ (ya da import'un getirdiği)
/// marka, host-global <see cref="TrendyolBrand"/> cache'ine idempotent UPSERT edilir (ExternalId eşleşmesi).
/// <para><b>Best-effort:</b> cache yazımı YAN etkidir — hata (ör. eşzamanlı insert yarışında unique çakışması)
/// çağıranın asıl işlemini (ürün kaydı/import) DÜŞÜRMEZ; uyarı olarak loglanır (N11 RunSafe felsefesi).
/// Anlamsız veri (boş/numerik-olmayan id, import sentinel "0", boş ad, üst sınırı aşan uzak değer) sessizce
/// atlanır — cache zenginleştirme fail-fast yeri değildir. Host bağlamı <see cref="ICurrentTenant.Change(Guid?)"/>
/// (null) ile garanti edilir (CarrierSeeder deseni; db-per-tenant'a karşı merkezilik).</para>
/// </summary>
public class TrendyolBrandCacheManager(
    IRepository<TrendyolBrand, Guid> repository,
    ICurrentTenant currentTenant,
    ILogger<TrendyolBrandCacheManager> logger)
    : ITransientDependency
{
    #region Fields

    private readonly IRepository<TrendyolBrand, Guid> _repository = repository;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly ILogger<TrendyolBrandCacheManager> _logger = logger;

    #endregion

    #region Methods

    /// <summary>Seçilen/ithal edilen markayı cache'e upsert eder (idempotent): yoksa ekler, ad/luxury değiştiyse
    /// tazeler. <paramref name="isLuxury"/> null = bilinmiyor (import yolu): insert'te false, update'te DOKUNULMAZ.
    /// Geçersiz/eksik veri atlanır; hata loglanır ama FIRLATILMAZ (çağıranın işlemi sürer).</summary>
    public virtual async Task UpsertAsync(string? externalId, string? name, bool? isLuxury = null)
    {
        var trimmedName = name?.Trim();
        if (!TryParseCacheable(externalId, trimmedName, out var id))
        {
            return;
        }

        try
        {
            // Host-global yazma → host'a sabitle (entity TenantId taşımaz; Change(null) db-per-tenant emniyeti).
            using (_currentTenant.Change(null))
            {
                var existing = await _repository.FindAsync(x => x.ExternalId == id);
                if (existing is null)
                {
                    await _repository.InsertAsync(new TrendyolBrand(id, trimmedName!, isLuxury ?? false), autoSave: true);
                    return;
                }

                var changed = false;
                if (!string.Equals(existing.Name, trimmedName, StringComparison.Ordinal))
                {
                    existing.SetName(trimmedName!);
                    changed = true;
                }

                if (isLuxury.HasValue && existing.IsLuxury != isLuxury.Value)
                {
                    existing.SetIsLuxury(isLuxury.Value);
                    changed = true;
                }

                if (changed)
                {
                    await _repository.UpdateAsync(existing, autoSave: true);
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort: cache yarışı/geçici DB hatası asıl kaydı düşürmesin — kök neden log'da görünür kalır.
            _logger.LogWarning(ex, "Trendyol marka cache upsert atlandı (ExternalId={ExternalId}).", id);
        }
    }

    /// <summary>Cache'e yazmaya değer mi + kanal-üründeki string BrandId'nin long çözümü: id POZİTİF numerik
    /// (Trendyol brandId long'dur; "0" = import'un "marka bilinmiyor" sentinel'i → elenir), ad dolu ve üst sınır
    /// içinde. Parse edilemeyen id cache'e HİÇ girmemeli.</summary>
    private static bool TryParseCacheable(string? externalId, string? name, out long id)
    {
        id = 0;
        var raw = externalId?.Trim();
        if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (!long.TryParse(raw, out id) || id <= 0)
        {
            return false;
        }

        return name.Length <= TrendyolBrandConsts.NameMaxLength;
    }

    #endregion
}
