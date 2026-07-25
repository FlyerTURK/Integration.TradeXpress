using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Vaults;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Çok-şirket güvenlik sınırı geçiş backfill'i — <see cref="ICompanyOwned"/>'a SONRADAN taşınan kayıtların
/// migration tarafından <c>Guid.Empty</c> bırakılan <c>CompanyId</c>'sini doldurur. İki AYRI sahip kuralı vardır:
///
/// <para><b>(1) YAPISAL kanıt</b> — <see cref="SubAccount"/>/<see cref="Vault"/>: sahip parent'tan okunur
/// (SubAccount→Account, Vault→Branch). Tahmin yok, kanıt var.</para>
///
/// <para><b>(2) POLİTİKA</b> — 7 EMTİA ailesi (Metal·Stone·Jewelry·Good·Scrap·Future·Service, görev #4):
/// emtianın parent'ı YOKTUR, sahibi kanıtlayan yapısal bağ da yok → sahip tenant'ın <b>merkez (HQ) şirketi</b>
/// seçilir. Bu bir POLİTİKA kararıdır, çıkarım değil: kaynağı, kataloğun eskiden TENANT-GENELİ olması ve
/// tenant-geneli bir kaydın doğal devralıcısının merkez şirket olmasıdır. Şüpheli her satır sessizce
/// taşınmaz — ATLANIR ve <c>LogWarning</c> ile raporlanır.</para>
///
/// <para><b>Neden migration DIŞINDA:</b> EF migration'ı non-nullable <c>CompanyId</c>'yi <c>Guid.Empty</c>
/// defaultValue ile ekler (mevcut satırlar önce boş kalır). Backfill SQL'i migration'a elle eklenemedi
/// (governance guard'ı Migrations düzenlemesini bloklar). Bunun yerine bu idempotent seeder, DbMigrator'ın
/// migrate'ten HEMEN SONRA çalıştırdığı akışta boş kalanları doldurur.</para>
///
/// <para><b>SIRA KRİTİK (emtia için) — İKİ YERDEN çağrılır:</b> yetimler, emtia seeder'ları çalışmadan ÖNCE
/// sahiplendirilmiş olmalıdır. Aksi halde seeder "bu şirkette kayıt yok" deyip TAZE varsayılan set açar,
/// kullanıcının düzenlediği satırlar sahipsiz/görünmez kalır ve sonraki koşularda kod artık dolu olduğu için
/// KALICI olarak atlanır — 2026-07-25'te canlıda bir kez yaşandı.
/// <list type="number">
/// <item>HOST geçişi (tüm tenant geçişlerinden önce biter) — hâlihazırda merkez şirketi olan tenant'ları kapar.</item>
/// <item>TENANT geçişi, emtia seeder'larından hemen önce — <b>zorunlu</b>: merkez şirket TENANT dalında
/// (<c>OrgSeeder</c>) kurulur, dolayısıyla şirketi HENÜZ olmayan bir tenant host geçişinde atlanır.
/// Tek başına host çağrısına güvenmek kod incelemesinde çürütüldü.</item>
/// </list>
/// Her iki çağrı da idempotenttir; yetim yoksa ucuz no-op.</para>
///
/// <para><b>İdempotent:</b> yalnız <c>CompanyId == Guid.Empty</c> satırlara dokunur; ikinci koşuda hiçbir şey
/// yapmaz. WHERE'siz toplu yazma YOK. Repository tabanlı (raw SQL YOK) → SQL Server + Sqlite test aynı yolu izler.</para>
///
/// <para><b>TÜM tenant'ları kapsar (multi-tenant):</b> geçiş backfill'i tenant-bağımsız bir veri düzeltmesidir.
/// Bu yüzden <see cref="IMultiTenant"/> ve <see cref="ICompanyScoped"/> filtreleri <c>Disable</c> edilerek çalışır —
/// hangi tenant/şirket context'inde tetiklenirse tetiklensin SİSTEMDEKİ TÜM boş satırları görür.</para>
/// </summary>
public class CompanyOwnedBackfiller : DomainService
{
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IRepository<Stone, Guid> _stoneRepository;
    private readonly IRepository<Jewelry, Guid> _jewelryRepository;
    private readonly IRepository<Good, Guid> _goodRepository;
    private readonly IRepository<Scrap, Guid> _scrapRepository;
    private readonly IRepository<Future, Guid> _futureRepository;
    private readonly IRepository<Service, Guid> _serviceRepository;
    private readonly IDataFilter _dataFilter;

    public CompanyOwnedBackfiller(
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<Account, Guid> accountRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Company, Guid> companyRepository,
        IRepository<Metal, Guid> metalRepository,
        IRepository<Stone, Guid> stoneRepository,
        IRepository<Jewelry, Guid> jewelryRepository,
        IRepository<Good, Guid> goodRepository,
        IRepository<Scrap, Guid> scrapRepository,
        IRepository<Future, Guid> futureRepository,
        IRepository<Service, Guid> serviceRepository,
        IDataFilter dataFilter)
    {
        _subAccountRepository = subAccountRepository;
        _accountRepository    = accountRepository;
        _vaultRepository      = vaultRepository;
        _branchRepository     = branchRepository;
        _companyRepository    = companyRepository;
        _metalRepository      = metalRepository;
        _stoneRepository      = stoneRepository;
        _jewelryRepository    = jewelryRepository;
        _goodRepository       = goodRepository;
        _scrapRepository      = scrapRepository;
        _futureRepository     = futureRepository;
        _serviceRepository    = serviceRepository;
        _dataFilter           = dataFilter;
    }

    /// <summary>Sistemdeki (tüm tenant'lar) boş (Guid.Empty) CompanyId taşıyan kayıtları doldurur:
    /// SubAccount/Vault parent'tan, emtia aileleri tenant'ın merkez şirketinden. Boş satır yoksa
    /// (temiz kurulum ya da ikinci koşu) ucuz no-op.</summary>
    public async Task BackfillAllTenantsAsync()
    {
        // Tenant + company filtreleri kapalı: tüm tenant'ların boş kayıtları tek koşuda görülür ve doldurulur.
        // ICompanyScoped ŞART: filtre anahtarı ICompanyOwned için de aynıdır; gerçek bir working company
        // seçiliyken (ya da yetkisiz kullanıcı sentinel'i Guid.Empty iken) yetim satırlar süzülüp backfill
        // SESSİZ no-op olurdu — doğruluk "kim çağırdı"ya bağlı kalmamalı.
        using (_dataFilter.Disable<IMultiTenant>())
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            await BackfillSubAccountsAsync();
            await BackfillVaultsAsync();
            await BackfillCommoditiesAsync();
        }
    }

    private async Task BackfillSubAccountsAsync()
    {
        var orphans = await AsyncExecuter.ToListAsync(
            (await _subAccountRepository.GetQueryableAsync()).Where(s => s.CompanyId == Guid.Empty));
        if (orphans.Count == 0)
        {
            return;
        }

        var companyByAccount = await MapCompanyByParentAsync(
            orphans.Select(s => s.AccountId),
            _accountRepository,
            a => a.Id,
            a => a.CompanyId);

        var skipped = 0;
        foreach (var sub in orphans)
        {
            if (companyByAccount.TryGetValue(sub.AccountId, out var companyId) && companyId != Guid.Empty)
            {
                sub.BackfillCompanyIfMissing(companyId);
                await _subAccountRepository.UpdateAsync(sub, autoSave: true);
                continue;
            }

            skipped++;   // parent bulunamadı/parent da sahipsiz → SESSİZ geçme (aşağıda raporlanır)
        }

        LogParentBackfillResult(nameof(SubAccount), orphans.Count - skipped, skipped);
    }

    private async Task BackfillVaultsAsync()
    {
        var orphans = await AsyncExecuter.ToListAsync(
            (await _vaultRepository.GetQueryableAsync()).Where(v => v.CompanyId == Guid.Empty));
        if (orphans.Count == 0)
        {
            return;
        }

        var companyByBranch = await MapCompanyByParentAsync(
            orphans.Select(v => v.BranchId),
            _branchRepository,
            b => b.Id,
            b => b.CompanyId);

        var skipped = 0;
        foreach (var vault in orphans)
        {
            if (companyByBranch.TryGetValue(vault.BranchId, out var companyId) && companyId != Guid.Empty)
            {
                vault.BackfillCompanyIfMissing(companyId);
                await _vaultRepository.UpdateAsync(vault, autoSave: true);
                continue;
            }

            skipped++;
        }

        LogParentBackfillResult(nameof(Vault), orphans.Count - skipped, skipped);
    }

    /// <summary>7 emtia ailesinin yetim satırlarını tenant'ın merkez şirketine sahiplendirir.</summary>
    private async Task BackfillCommoditiesAsync()
    {
        var headquartersByTenant = await MapHeadquartersByTenantAsync();
        if (headquartersByTenant.Count == 0)
        {
            return;   // hiç şirket yok (temiz host kurulumu) → sahiplendirilecek bir şey de yok
        }

        // Sahiplendirme delegesi çağrı yerinden geçilir: ICompanyOwned bilinçli olarak SALT-OKUR bir güvenlik
        // marker'ıdır — geçiş dönemine ait bir mutator'ı oraya eklemek marker'ı kirletir ve gelecekteki her
        // ICompanyOwned entity'sine ihtiyacı olmayan bir metodu dayatırdı.
        await BackfillCommodityFamilyAsync(_metalRepository,   m => m.Code, (e, id) => e.BackfillCompanyIfMissing(id), headquartersByTenant);
        await BackfillCommodityFamilyAsync(_stoneRepository,   s => s.Code, (e, id) => e.BackfillCompanyIfMissing(id), headquartersByTenant);
        await BackfillCommodityFamilyAsync(_jewelryRepository, j => j.Code, (e, id) => e.BackfillCompanyIfMissing(id), headquartersByTenant);
        await BackfillCommodityFamilyAsync(_goodRepository,    g => g.Code, (e, id) => e.BackfillCompanyIfMissing(id), headquartersByTenant);
        await BackfillCommodityFamilyAsync(_scrapRepository,   s => s.Code, (e, id) => e.BackfillCompanyIfMissing(id), headquartersByTenant);
        await BackfillCommodityFamilyAsync(_futureRepository,  f => f.Code, (e, id) => e.BackfillCompanyIfMissing(id), headquartersByTenant);
        await BackfillCommodityFamilyAsync(_serviceRepository, s => s.Code, (e, id) => e.BackfillCompanyIfMissing(id), headquartersByTenant);
    }

    /// <summary>
    /// Tek bir emtia ailesinin yetimlerini sahiplendirir.
    ///
    /// <para><b>Soft-delete'li yetim de DOLDURULUR</b> (atlanmaz): seeder silinmiş kodu "mevcut" sayıp
    /// diriltmesin diye kodu görebilmesi gerekir (<c>MetalSeeder</c>'ın diriltme-önleme kuralı). Silinmiş satır
    /// benzersizlik indeksine girmez (<c>[IsDeleted] = 0</c> filtreli) → çakışma riski yok.</para>
    ///
    /// <para><b>Çakışma varsa ATLANIR</b>: hedef şirkette aynı kod CANLI ise satıra dokunulmaz ve uyarı loglanır.
    /// Kod türetme/suffix ekleme YOK — sessiz veri mutasyonu yapılmaz.</para>
    /// </summary>
    private async Task BackfillCommodityFamilyAsync<TCommodity>(
        IRepository<TCommodity, Guid> repository,
        Func<TCommodity, string> codeSelector,
        Action<TCommodity, Guid> assignCompany,
        Dictionary<Guid, Guid> headquartersByTenant)
        where TCommodity : class, IEntity<Guid>, IMultiTenant, ICompanyOwned, ISoftDelete
    {
        // Soft-delete filtresi yalnız YETİM OKUMASI için kapalı (yukarıdaki gerekçe).
        // Host satırı (TenantId=null) kapsam DIŞI: host'ta şirket yoktur, benzersizlik indeksi de host'u dışlar.
        List<TCommodity> orphans;
        using (_dataFilter.Disable<ISoftDelete>())
        {
            orphans = await AsyncExecuter.ToListAsync(
                (await repository.GetQueryableAsync())
                    .Where(e => e.CompanyId == Guid.Empty && e.TenantId != null));
        }

        if (orphans.Count == 0)
        {
            return;
        }

        var familyName = typeof(TCommodity).Name;
        var occupiedCodes = await BuildOccupiedCodeSetAsync(repository, codeSelector, orphans, headquartersByTenant);

        var filled = 0;
        var skippedNoHeadquarters = new List<string>();
        var skippedCodeTaken = new List<string>();

        foreach (var orphan in orphans)
        {
            var code = codeSelector(orphan).ToUpperInvariant();

            if (!headquartersByTenant.TryGetValue(orphan.TenantId!.Value, out var companyId))
            {
                // Şirket ÜRETMEYİZ — org kurulumu OrgSeeder'ın işi. Kimliği logla ki operatör bulabilsin.
                skippedNoHeadquarters.Add($"{orphan.TenantId}/{code}");
                continue;
            }

            // Çakışma yalnız CANLI satırlar için engel: silinmiş satır benzersizlik indeksine girmez.
            if (!orphan.IsDeleted && !occupiedCodes.Add((companyId, code)))
            {
                skippedCodeTaken.Add($"{orphan.TenantId}/{companyId}/{code}");
                continue;
            }

            assignCompany(orphan, companyId);

            // NOT (kod incelemesi endişesi): soft-delete'li satırda ABP UpdateAsync'i silme yoluna sokar.
            // Silme damgaları YENİDEN yazılmamalı — kullanıcının gerçek silme anı korunmalı. Bunu varsaymak
            // yerine mekanik olarak bağladık: CommodityCompanyBackfillerTests, silinmiş yetimin doldurulduktan
            // sonra hâlâ IsDeleted olduğunu VE DeletionTime'ının değişmediğini assert eder.
            await repository.UpdateAsync(orphan, autoSave: true);
            filled++;
        }

        LogFamilyResult(familyName, filled, skippedNoHeadquarters, skippedCodeTaken);
    }

    /// <summary>Hedef şirketlerde HÂLİHAZIRDA kullanılan (şirket, kod) çiftleri — çakışma ön-kontrolü.
    /// Soft-delete filtresi AÇIK: silinmiş satır kod slotunu işgal etmez (indeks <c>[IsDeleted] = 0</c> filtreli).</summary>
    private async Task<HashSet<(Guid CompanyId, string Code)>> BuildOccupiedCodeSetAsync<TCommodity>(
        IRepository<TCommodity, Guid> repository,
        Func<TCommodity, string> codeSelector,
        List<TCommodity> orphans,
        Dictionary<Guid, Guid> headquartersByTenant)
        where TCommodity : class, IEntity<Guid>, IMultiTenant, ICompanyOwned, ISoftDelete
    {
        var targetCompanyIds = orphans
            .Where(o => o.TenantId != null && headquartersByTenant.ContainsKey(o.TenantId.Value))
            .Select(o => headquartersByTenant[o.TenantId!.Value])
            .Distinct()
            .ToList();

        if (targetCompanyIds.Count == 0)
        {
            return new HashSet<(Guid, string)>();
        }

        var existing = await AsyncExecuter.ToListAsync(
            (await repository.GetQueryableAsync()).Where(e => targetCompanyIds.Contains(e.CompanyId)));

        return existing
            .Select(e => (e.CompanyId, codeSelector(e).ToUpperInvariant()))
            .ToHashSet();
    }

    /// <summary>
    /// Tenant → merkez (HQ) şirket haritası. Tenant başına BİRDEN FAZLA canlı HQ yapısal olarak mümkündür
    /// (DB'de unique indeks YOK; tekillik yalnız <c>CompanyAppService</c>'te zorlanır) → <c>SingleOrDefault</c>
    /// KULLANILMAZ; deterministik seçim yapılır: DisplayOrder → CreationTime → Id.
    /// <para>Soft-delete filtresi AÇIK bırakılır: silinmiş şirkete sahiplik atamak kaydı kalıcı erişilemez yapardı.
    /// Aynı gerekçeyle PASİF şirket de aday değildir; hiç aktif merkez yoksa merkez olmayan aktif şirketlere
    /// DÜŞÜLMEZ (fail-closed) — sahiplik bir güvenlik kararıdır, tahminle atanmaz, satır atlanıp raporlanır.</para>
    /// </summary>
    private async Task<Dictionary<Guid, Guid>> MapHeadquartersByTenantAsync()
    {
        var headquarters = await AsyncExecuter.ToListAsync(
            (await _companyRepository.GetQueryableAsync())
                .Where(c => c.TenantId != null && c.IsHeadquarters && c.IsActive));

        return headquarters
            .GroupBy(c => c.TenantId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(c => c.DisplayOrder)
                      .ThenBy(c => c.CreationTime)
                      .ThenBy(c => c.Id)
                      .First()
                      .Id);
    }

    /// <summary>Sonucu görünür kılar — atlanan satır SESSİZ kalmaz (eski backfill'in zaafı buydu).
    /// Atlananlarda KİMLİK de loglanır (tenant/şirket/kod): operatör salt sayıyla kaydı bulamaz, ve atlanan
    /// satır tam da sınıfın önlemek için var olduğu "görünmez katalog" durumudur.</summary>
    private void LogFamilyResult(
        string familyName, int filled, List<string> skippedNoHeadquarters, List<string> skippedCodeTaken)
    {
        if (filled > 0)
        {
            Logger.LogInformation(
                "Emtia sahiplendirme [{Family}]: {Filled} yetim satır tenant merkez şirketine atandı.",
                familyName, filled);
        }

        if (skippedNoHeadquarters.Count > 0)
        {
            Logger.LogWarning(
                "Emtia sahiplendirme [{Family}]: {Skipped} yetim satır ATLANDI — tenant'ın canlı merkez şirketi "
                + "yok, kayıtlar GÖRÜNMEZ kalıyor. Etkilenenler (tenant/kod): {Items}",
                familyName, skippedNoHeadquarters.Count, string.Join(", ", skippedNoHeadquarters));
        }

        if (skippedCodeTaken.Count > 0)
        {
            Logger.LogWarning(
                "Emtia sahiplendirme [{Family}]: {Skipped} yetim satır ATLANDI — hedef şirkette aynı kod zaten "
                + "CANLI, kayıtlar GÖRÜNMEZ kalıyor. Etkilenenler (tenant/şirket/kod): {Items}",
                familyName, skippedCodeTaken.Count, string.Join(", ", skippedCodeTaken));
        }
    }

    /// <summary>Parent-kanıtlı kol (SubAccount/Vault) için sonuç raporu — emtia koluyla simetrik.</summary>
    private void LogParentBackfillResult(string entityName, int filled, int skipped)
    {
        if (filled > 0)
        {
            Logger.LogInformation(
                "Sahiplik backfill [{Entity}]: {Filled} satır parent'tan dolduruldu.", entityName, filled);
        }

        if (skipped > 0)
        {
            Logger.LogWarning(
                "Sahiplik backfill [{Entity}]: {Skipped} satır ATLANDI — parent bulunamadı ya da parent da sahipsiz.",
                entityName, skipped);
        }
    }

    /// <summary>Verilen parent id kümesi için (id → CompanyId) haritasını çıkarır (tek sorgu; distinct).</summary>
    private async Task<Dictionary<Guid, Guid>> MapCompanyByParentAsync<TParent>(
        IEnumerable<Guid> parentIds,
        IRepository<TParent, Guid> parentRepository,
        Func<TParent, Guid> idSelector,
        Func<TParent, Guid> companySelector)
        where TParent : class, IEntity<Guid>
    {
        var ids = parentIds.Distinct().ToList();
        var parents = await AsyncExecuter.ToListAsync(
            (await parentRepository.GetQueryableAsync()).Where(p => ids.Contains(p.Id)));
        return parents.ToDictionary(idSelector, companySelector);
    }
}
