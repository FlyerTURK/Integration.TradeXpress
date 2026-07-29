using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// N11 kategori ağacı + komisyon mutabakatı ve BAYATLIK KAPISI — arka plan işçisi (ve açılış) HOST bağlamında
/// çağırır. <b>AppService DEĞİL</b> çünkü <c>[Authorize]</c> interceptor'ı kullanıcısız worker'da patlardı
/// (<see cref="EtsyTaxonomies.EtsyTaxonomySyncManager"/> ikizi).
///
/// <para><b>Akış (2026-07-28 Hakan tasarımı):</b> (1) DB'deki en son senkron damgası 1 günden yeniyse HİÇBİR ŞEY
/// yapılmaz — N11'e istek bile gitmez, test açılışları boşuna ağaç çekmez. (2) Bayatsa ağaç KOMPLE çekilir.
/// (3) Çekilen kategori sayısı DB'dekiyle (MEGA HARİÇ) karşılaştırılıp loglanır. (4) Gerçek fark varsa yazılır.
/// (5) Komisyonlar aynı turda uygulanır — kullanıcı hiçbir düğmeye basmaz. (6) Tur BAŞARIYLA bitince mega
/// satırlarına damga atılır.</para>
///
/// <para><b>Damga neden mega satırlarında:</b> mega'lar N11'den GELMEZ (sentetik üst katman), her zaman vardır ve
/// dış veriyle çakışmaz — "son mutabakat anı"nı taşımak için doğal yer. Damga, veri DEĞİŞMESE de atılır: amaç
/// "veri değişti" demek değil, "N11 ile konuştuk" demektir. Atılmazsa sistem her açılışta yeniden çeker.</para>
///
/// <para><b>UoW:</b> worker'da ambient UoW yoktur → yönetim tamamen burada. Kısa read UoW (kapı) → HTTP çekimi
/// UoW DIŞINDA (uzun süren istek DbContext'i tutmasın) → write UoW (upsert + komisyon + damga).</para>
///
/// <para><b>Tenant:</b> <see cref="N11Category"/> multi-tenant DEĞİL ve kimlik config'ten gelir; host bağlamına
/// sabitlemek (<c>CurrentTenant.Change(null)</c>) yeterlidir — tenant döngüsü/filtre kapatma gerekmez.</para>
/// </summary>
public class N11CategorySyncManager : DomainService
{
    /// <summary>Config yoksa varsayılan bayatlık eşiği/worker periyodu (saat).</summary>
    private const int DefaultSyncIntervalHours = 24;

    /// <summary>Bayatlık eşiği config anahtarı.</summary>
    private const string SyncIntervalConfigKey = "N11:CategorySync:SyncIntervalHours";

    /// <summary>Sorunlu satırlardan log'a yazılacak örnek sayısı (tamamı binlerce olabilir).</summary>
    private const int LoggedIssueSampleSize = 10;

    private readonly IRepository<N11Category, Guid> _repository;
    private readonly IN11CategoryClient _client;
    private readonly N11CategoryMegaGrouper _megaGrouper;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IClock _clock;
    private readonly IConfiguration _configuration;

    public N11CategorySyncManager(
        IRepository<N11Category, Guid> repository,
        IN11CategoryClient client,
        N11CategoryMegaGrouper megaGrouper,
        IUnitOfWorkManager uowManager,
        IClock clock,
        IConfiguration configuration)
    {
        _repository = repository;
        _client = client;
        _megaGrouper = megaGrouper;
        _uowManager = uowManager;
        _clock = clock;
        _configuration = configuration;
    }

    /// <summary>Bayatlık eşiği / worker periyodu (config, yoksa veya ≤0 ise 24 saat).</summary>
    public TimeSpan ResolveSyncInterval()
    {
        var hours = _configuration.GetValue<int?>(SyncIntervalConfigKey) ?? DefaultSyncIntervalHours;
        if (hours <= 0)
        {
            hours = DefaultSyncIntervalHours;
        }

        return TimeSpan.FromHours(hours);
    }

    /// <summary>Tablo boşsa ya da son senkron damgası <paramref name="threshold"/>'dan eskiyse mutabakatı çalıştırır
    /// (true); aksi halde N11'e HİÇ gitmeden atlar (false).
    ///
    /// <para>Damga hiç atılmamışsa (<c>MAX(LastSyncedAt)</c> null — bu alan eklenmeden önce senkronlanmış DB)
    /// bayat sayılır: bir kez daha mutabakat yapılır ve damga oturur.</para></summary>
    public virtual async Task<bool> SyncIfStaleAsync(TimeSpan threshold, CancellationToken cancellationToken = default)
    {
        bool shouldSync;
        using (CurrentTenant.Change(null))
        using (var readUow = _uowManager.Begin(requiresNew: true))
        {
            var query = await _repository.GetQueryableAsync();
            if (!await AsyncExecuter.AnyAsync(query))
            {
                shouldSync = true;
            }
            else
            {
                var lastSyncedAt = await AsyncExecuter.MaxAsync(query, x => x.LastSyncedAt);
                shouldSync = lastSyncedAt is not { } stampedAt || (_clock.Now - stampedAt) >= threshold;
            }

            await readUow.CompleteAsync(cancellationToken);
        }

        if (!shouldSync)
        {
            return false;
        }

        await ReconcileAsync(cancellationToken);
        return true;
    }

    /// <summary>Ağacı komple çeker, farkı yazar, komisyonları uygular, turu damgalar. Değişen satır sayısını döner.
    /// Kimlik yoksa dostane <see cref="BusinessException"/> (worker yutar + loglar).</summary>
    public virtual async Task<int> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var appKey = _configuration["N11:CategorySync:AppKey"];
        var appSecret = _configuration["N11:CategorySync:AppSecret"];
        if (string.IsNullOrWhiteSpace(appKey) || string.IsNullOrWhiteSpace(appSecret))
        {
            throw new BusinessException("TradeXpress:N11:CategorySyncCredentialsMissing");
        }

        // HTTP hiçbir UoW içinde değil: tam ağaç çekimi uzun sürer, DbContext'i o süre boyunca tutmak yanlış.
        var nodes = await _client.GetCategoryTreeAsync(appKey, appSecret);

        using (CurrentTenant.Change(null))
        using (var writeUow = _uowManager.Begin(requiresNew: true))
        {
            var existing = (await _repository.GetListAsync()).ToDictionary(x => x.ExternalId, StringComparer.Ordinal);
            var toInsert = new List<N11Category>();
            var toUpdate = new List<N11Category>();

            foreach (var node in nodes)
            {
                if (existing.TryGetValue(node.ExternalId, out var entity))
                {
                    if (ApplyChanges(entity, node))
                    {
                        toUpdate.Add(entity);
                    }
                }
                else
                {
                    toInsert.Add(new N11Category(node.ExternalId, node.ParentExternalId, node.Name, node.IsLeaf, node.LastModifiedExternal));
                }
            }

            LogCountComparison(nodes, existing.Values);

            if (toInsert.Count > 0)
            {
                await _repository.InsertManyAsync(toInsert, autoSave: true);
            }

            if (toUpdate.Count > 0)
            {
                await _repository.UpdateManyAsync(toUpdate, autoSave: true);
            }

            // 79 top N11'den kök olarak gelir → sentetik mega katmanı yeniden uygulanır (breadcrumb üst seviyesi).
            await _megaGrouper.EnsureAsync();

            var commissionUpdates = await ApplyCommissionsAsync();
            await StampSyncAsync(cancellationToken);

            await writeUow.CompleteAsync(cancellationToken);

            Logger.LogInformation(
                "N11 kategori mutabakatı: +{Inserted} eklendi, ~{Updated} güncellendi, komisyon ~{Commission} satır ({Total} düğüm çekildi).",
                toInsert.Count, toUpdate.Count, commissionUpdates, nodes.Count);

            return toInsert.Count + toUpdate.Count + commissionUpdates;
        }
    }

    /// <summary>Çekilen ile kayıtlı kategori sayısını MEGA HARİÇ karşılaştırıp loglar (teşhis).
    ///
    /// <para>Bu sayı yazma kararını VERMEZ — karar gerçek diff'indir. Sebep: N11 bir kategoriyi yeniden
    /// adlandırdığında ya da taşıdığında sayı değişmez; sayıya bakıp atlasaydık o değişiklikler bize hiç
    /// yansımazdı. Diff zaten bellek-içi ve ucuz; pahalı olan HTTP çekimi bu noktada çoktan yapılmış oluyor.</para></summary>
    private void LogCountComparison(IReadOnlyList<N11CategoryNode> nodes, IEnumerable<N11Category> existing)
    {
        var fetchedCount = nodes.Select(n => n.ExternalId).Distinct(StringComparer.Ordinal).Count();
        var storedNonMegaCount = existing.Count(c => !N11MegaCategories.IsMega(c.ExternalId));

        Logger.LogInformation(
            "N11 kategori sayımı (mega hariç): N11'de {Fetched}, bizde {Stored}, fark {Difference}.",
            fetchedCount, storedNonMegaCount, fetchedCount - storedNonMegaCount);
    }

    /// <summary>Gömülü komisyon TSV'sini yapraklara uygular — DEĞİŞENİ yazar. Güncellenen satır sayısını döner.
    ///
    /// <para>Komisyon oranları N11'de kanala özel değil, kategoriye aittir; bu yüzden kullanıcıya bir "içe aktar"
    /// düğmesi sunulmaz, mutabakatın parçasıdır. Diff-farkındalık şart: bu iş günde bir kez otomatik koşuyor,
    /// koşulsuz yazım binlerce satırı boş yere denetim kaydına sokardı.</para>
    ///
    /// <para>Eşleşmeyen/çakışan/geçersiz satırlar artık ekranda görünmediği için LOG'a yazılır — sessizce yutulmaz.</para></summary>
    private async Task<int> ApplyCommissionsAsync()
    {
        var parse = N11CategoryCommissionImporter.ParseTsv(N11CategoryCommissionImporter.ReadEmbeddedTsv());
        var categories = await _repository.GetListAsync();
        var match = N11CategoryCommissionImporter.Match(parse.Rows, categories);

        var changed = new List<N11Category>();
        foreach (var (category, row) in match.Matches)
        {
            if (ApplyCommission(category, row))
            {
                changed.Add(category);
            }
        }

        if (changed.Count > 0)
        {
            await _repository.UpdateManyAsync(changed, autoSave: true);
        }

        LogCommissionIssues(parse, match);
        return changed.Count;
    }

    /// <summary>Komisyon alanlarını yalnız GERÇEKTEN farklıysa yazar; yazıldıysa true.</summary>
    private static bool ApplyCommission(N11Category category, N11CommissionRow row)
    {
        if (category.CommissionRate == row.CommissionRate
            && category.MarketingFeeRate == row.MarketingFeeRate
            && category.MarketplaceFeeRate == row.MarketplaceFeeRate
            && category.PayoutDays == row.PayoutDays)
        {
            return false;
        }

        category.SetCommission(row.CommissionRate, row.MarketingFeeRate, row.MarketplaceFeeRate, row.PayoutDays);
        return true;
    }

    private void LogCommissionIssues(N11CommissionParseResult parse, N11CommissionMatchResult match)
    {
        var issues = match.Unmatched.Concat(match.Conflicts).Concat(parse.InvalidRows).ToList();
        if (issues.Count == 0)
        {
            return;
        }

        Logger.LogWarning(
            "N11 komisyon: {IssueCount} satır uygulanamadı (eşleşmeyen {Unmatched}, çakışan {Conflicts}, geçersiz {Invalid}). İlk {SampleSize}: {Sample}",
            issues.Count,
            match.Unmatched.Count,
            match.Conflicts.Count,
            parse.InvalidRows.Count,
            LoggedIssueSampleSize,
            string.Join(" | ", issues.Take(LoggedIssueSampleSize)));
    }

    /// <summary>Turu damgalar: mega satırlarına bu turun saatini yazar.
    ///
    /// <para>Damga, tek satır bile değişmemiş olsa ATILIR — kapının kapanma koşulu budur. Yalnız çekim/yazım
    /// hata verirse atılmaz (istisna UoW'u geri alır) ve bir sonraki tur yeniden dener.</para></summary>
    private async Task StampSyncAsync(CancellationToken cancellationToken)
    {
        var all = await _repository.GetListAsync();
        var megas = all.Where(c => N11MegaCategories.IsMega(c.ExternalId)).ToList();
        if (megas.Count == 0)
        {
            // Damgalanacak satır yoksa kapı bir daha asla kapanmaz → sessiz kalmak yerine fail-fast.
            throw new BusinessException("TradeXpress:N11:CategoryStampTargetMissing");
        }

        var stampedAt = _clock.Now;
        foreach (var mega in megas)
        {
            mega.MarkSynced(stampedAt);
        }

        await _repository.UpdateManyAsync(megas, autoSave: true);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Sync upsert: değişen alanları uygular; değişiklik olduysa true.</summary>
    private static bool ApplyChanges(N11Category entity, N11CategoryNode node)
    {
        var changed = false;
        if (!string.Equals(entity.Name, node.Name, StringComparison.Ordinal))
        {
            entity.SetName(node.Name);
            changed = true;
        }

        if (ShouldApplyParent(entity, node))
        {
            entity.SetParent(node.ParentExternalId);
            changed = true;
        }

        if (entity.IsLeaf != node.IsLeaf)
        {
            entity.SetIsLeaf(node.IsLeaf);
            changed = true;
        }

        // Yalnız DOLU gelirse yazılır: REST yolu bu alanı her düğüm için null döndürüyor; koşulsuz karşılaştırma
        // daha önce SOAP'tan gelmiş bir tarihi her turda "değişti" sayıp gereksiz UPDATE üretirdi.
        if (node.LastModifiedExternal is not null && entity.LastModifiedExternal != node.LastModifiedExternal)
        {
            entity.SetLastModifiedExternal(node.LastModifiedExternal);
            changed = true;
        }

        return changed;
    }

    /// <summary>Üst kategori farkı GERÇEK bir değişiklik mi?
    ///
    /// <para>N11 79 top-level kategoriyi köksüz (<c>parentId=null</c>) döndürür; biz onları sentetik mega
    /// katmanına bağlarız. Bu farkı değişiklik saymak, her turda 79 satırı "mega → null" diye yazıp hemen
    /// ardından mega grouper'ın aynı 79'u geri çevirmesine yol açardı: hiçbir şey değişmese bile tur başına
    /// 158 gereksiz UPDATE. O yüzden "gelen null + mevcut mega" durumu değişiklik SAYILMAZ.</para></summary>
    private static bool ShouldApplyParent(N11Category entity, N11CategoryNode node)
    {
        if (string.Equals(entity.ParentExternalId, node.ParentExternalId, StringComparison.Ordinal))
        {
            return false;
        }

        if (node.ParentExternalId is null && N11MegaCategories.IsMega(entity.ParentExternalId))
        {
            return false;
        }

        return true;
    }
}
