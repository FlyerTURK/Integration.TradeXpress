using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Vouchers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// <see cref="CompanyOwnedBackfiller"/>'ın EMTİA kolu — 7 aile (Metal·Stone·Jewelry·Good·Scrap·Future·Service)
/// görev #4 ile <c>ICompanyOwned</c>'a taşındı; migration <c>CompanyId</c>'yi <c>Guid.Empty</c> bırakıyor.
///
/// <para><b>Korunan senaryo (canlıda 2026-07-25'te yaşandı):</b> yetimler sahiplendirilmeden emtia seeder'ları
/// koşarsa seeder "bu şirkette kayıt yok" deyip TAZE varsayılan set açar; kullanıcının DÜZENLEDİĞİ satırlar
/// sahipsiz/görünmez kalır. Backfill host dalında, seeder'lardan önce koşarak bunu yapısal olarak engeller.</para>
///
/// <para>Sahip kuralı SubAccount/Vault'tan FARKLIDIR: emtianın parent'ı yok → sahip POLİTİKA ile seçilir
/// (tenant'ın merkez şirketi). Şüpheli satır taşınmaz, ATLANIR. KIRMIZIYSA testi gevşetme, kök nedeni düzelt.</para>
///
/// <para>Sqlite test DB'si model-tabanlı (CreateTables) kurulduğundan migration'ın <c>defaultValue</c>'su
/// yoktur → yetim durumu ham SQL ile SİMÜLE edilir (<c>CompanyOwnedBackfillerTests</c> deseni). Benzersizlik
/// indeksleri Sqlite'ta da zorlanır: kurulum kodları çakışmayacak şekilde üretilir.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class CommodityCompanyBackfillerTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly CompanyOwnedBackfiller _backfiller;
    private readonly IRepository<Metal, Guid> _metals;
    private readonly IRepository<Stone, Guid> _stones;
    private readonly IRepository<Jewelry, Guid> _jewelries;
    private readonly IRepository<Good, Guid> _goods;
    private readonly IRepository<Scrap, Guid> _scraps;
    private readonly IRepository<Future, Guid> _futures;
    private readonly IRepository<Service, Guid> _services;
    private readonly IRepository<Company, Guid> _companies;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly IDbContextProvider<TradeXpressDbContext> _dbContextProvider;
    private readonly IDataFilter _dataFilter;

    public CommodityCompanyBackfillerTests()
    {
        _backfiller        = GetRequiredService<CompanyOwnedBackfiller>();
        _metals            = GetRequiredService<IRepository<Metal, Guid>>();
        _stones            = GetRequiredService<IRepository<Stone, Guid>>();
        _jewelries         = GetRequiredService<IRepository<Jewelry, Guid>>();
        _goods             = GetRequiredService<IRepository<Good, Guid>>();
        _scraps            = GetRequiredService<IRepository<Scrap, Guid>>();
        _futures           = GetRequiredService<IRepository<Future, Guid>>();
        _services          = GetRequiredService<IRepository<Service, Guid>>();
        _companies         = GetRequiredService<IRepository<Company, Guid>>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _currentTenant     = GetRequiredService<ICurrentTenant>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
        _dbContextProvider = GetRequiredService<IDbContextProvider<TradeXpressDbContext>>();
        _dataFilter        = GetRequiredService<IDataFilter>();
    }

    /// <summary>7 ailenin HEPSİ merkez şirkete sahiplenir; ikinci koşu değeri bozmaz (idempotent).</summary>
    [Fact]
    public async Task Backfills_all_seven_commodity_families_to_headquarters_and_is_idempotent()
    {
        var scenario = await SeedAllFamiliesAsync("B7");

        await ForceEmptyCompanyAsync(scenario);
        await RunBackfillAsync(scenario.TenantId);

        await AssertAllOwnedByAsync(scenario, scenario.CompanyId);

        // İkinci koşu: artık yetim yok → değerler aynen kalmalı.
        await RunBackfillAsync(scenario.TenantId);
        await AssertAllOwnedByAsync(scenario, scenario.CompanyId);
    }

    /// <summary>Tek koşu TÜM tenant'ları kapsar (Disable&lt;IMultiTenant&gt;) — biri tetiklerken diğeri unutulmaz.</summary>
    [Fact]
    public async Task Backfills_every_tenant_in_a_single_run()
    {
        var first  = await SeedAllFamiliesAsync("B7A");
        var second = await SeedAllFamiliesAsync("B7B");

        await ForceEmptyCompanyAsync(first);
        await ForceEmptyCompanyAsync(second);

        // Yalnız BİRİNCİ tenant'ın context'inde koşulur — ikincisi de dolmalı.
        await RunBackfillAsync(first.TenantId);

        await AssertAllOwnedByAsync(first, first.CompanyId);
        await AssertAllOwnedByAsync(second, second.CompanyId);
    }

    /// <summary>Yetim yokken (temiz kurulum / ikinci koşu) mevcut sahiplikler korunur.</summary>
    [Fact]
    public async Task Clean_install_without_orphans_is_noop()
    {
        var scenario = await SeedAllFamiliesAsync("B7N");

        await RunBackfillAsync(scenario.TenantId);

        await AssertAllOwnedByAsync(scenario, scenario.CompanyId);
    }

    /// <summary>Tenant'ın CANLI merkez şirketi yoksa satır ATLANIR — şirket UYDURULMAZ, exception da atılmaz.</summary>
    [Fact]
    public async Task Skips_orphan_when_tenant_has_no_live_headquarters()
    {
        var scenario = await SeedAllFamiliesAsync("B7H");

        await ForceEmptyCompanyAsync(scenario);

        // Tek şirketi soft-delete et → tenant'ın canlı HQ'su kalmaz.
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(scenario.TenantId))
            {
                var company = await _companies.GetAsync(scenario.CompanyId);
                await _companies.DeleteAsync(company, autoSave: true);
            }
        });

        // POZİTİF KONTROL: merkezi DURAN başka bir tenant — backfill'in gerçekten KOŞTUĞUNU kanıtlar.
        // Onsuz bu test, backfill hiç çalışmasa da (ör. çağrı düşse) yeşil kalırdı (kod incelemesi bulgusu).
        var control = await SeedAllFamiliesAsync("B7HC");
        await ForceEmptyCompanyAsync(control);

        await RunBackfillAsync(scenario.TenantId);

        // Satır sahipsiz KALIR (yanlış sahibe atanmaktansa boş kalması yeğdir).
        var scrap = await ReadAcrossFiltersAsync(_scraps, scenario.ScrapId);
        scrap.CompanyId.ShouldBe(Guid.Empty);

        await AssertAllOwnedByAsync(control, control.CompanyId);   // koşu gerçekleşti
    }

    /// <summary>Tenant'ta birden fazla canlı merkez varsa seçim DETERMİNİSTİKtir (DisplayOrder önce).
    /// DB'de HQ tekilliğini zorlayan unique indeks YOK → SingleOrDefault kullanılamaz.</summary>
    [Fact]
    public async Task Picks_deterministic_headquarters_when_tenant_has_multiple()
    {
        var scenario = await SeedAllFamiliesAsync("B7M");

        await ForceEmptyCompanyAsync(scenario);

        // İKİNCİ bir merkez şirket — DisplayOrder daha DÜŞÜK olduğu için kazanmalı.
        var winnerId = await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(scenario.TenantId))
            {
                var winner = new Company(
                    "B7MWIN", "B7M Winner", SimpleGuidGenerator.Instance.Create(), scenario.UnitId,
                    isHeadquarters: true);
                winner.SetDisplayOrder(0);

                var seeded = await _companies.GetAsync(scenario.CompanyId);
                seeded.SetDisplayOrder(5);
                await _companies.UpdateAsync(seeded, autoSave: true);

                var inserted = await _companies.InsertAsync(winner, autoSave: true);
                return inserted.Id;
            }
        });

        await RunBackfillAsync(scenario.TenantId);

        var scrap = await ReadAcrossFiltersAsync(_scraps, scenario.ScrapId);
        scrap.CompanyId.ShouldBe(winnerId);
    }

    /// <summary>Hedef şirkette aynı kod CANLI ise yetim ATLANIR — benzersizlik indeksi ihlal edilmez,
    /// kod türetme/suffix ile sessiz veri mutasyonu yapılmaz.</summary>
    [Fact]
    public async Task Skips_orphan_when_target_company_already_has_the_same_live_code()
    {
        var scenario = await SeedAllFamiliesAsync("B7C");

        // SIRA ÖNEMLİ: önce yetim adayını boşalt — kod slotunu bıraksın (benzersizlik indeksi
        // (TenantId, CompanyId, Code) Sqlite'ta da zorlanıyor), SONRA merkez şirkete aynı kodla canlı satır ekle.
        await ExecuteRawAsync("UPDATE AppServices SET CompanyId = {0} WHERE Id = {1}", Guid.Empty, scenario.ServiceId);

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(scenario.TenantId))
            {
                await _services.InsertAsync(
                    new Service(scenario.ServiceCode, "Çakışan Hizmet", scenario.CompanyId), autoSave: true);
            }
        });

        await RunBackfillAsync(scenario.TenantId);

        var service = await ReadAcrossFiltersAsync(_services, scenario.ServiceId);
        service.CompanyId.ShouldBe(Guid.Empty);   // dokunulmadı

        // POZİTİF KONTROL: aynı senaryodaki DİĞER aileler (kod çakışması yok) sahiplendirilmiş olmalı —
        // yoksa test "backfill hiç çalışmadı" hâlinde de yeşil kalırdı (kod incelemesi bulgusu).
        (await ReadAcrossFiltersAsync(_metals, scenario.MetalId)).CompanyId.ShouldBe(scenario.CompanyId);
        (await ReadAcrossFiltersAsync(_scraps, scenario.ScrapId)).CompanyId.ShouldBe(scenario.CompanyId);
    }

    /// <summary>Soft-delete edilmiş yetim de DOLDURULUR: seeder silinmiş kodu göremezse kullanıcının
    /// sildiği kaydı DİRİLTİR (MetalSeeder'ın diriltme-önleme kuralının ön koşulu).</summary>
    [Fact]
    public async Task Backfills_soft_deleted_orphans_so_the_seeder_cannot_resurrect_them()
    {
        var scenario = await SeedAllFamiliesAsync("B7D");

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(scenario.TenantId))
            {
                var scrap = await _scraps.GetAsync(scenario.ScrapId);
                await _scraps.DeleteAsync(scrap, autoSave: true);
            }
        });

        await ExecuteRawAsync("UPDATE AppScraps SET CompanyId = {0} WHERE Id = {1}", Guid.Empty, scenario.ScrapId);

        var before = await ReadSoftDeletedAsync(_scraps, scenario.ScrapId);
        before.CompanyId.ShouldBe(Guid.Empty);      // ön koşul: gerçekten yetim
        before.IsDeleted.ShouldBeTrue();
        var deletionTimeBefore = before.DeletionTime;

        await RunBackfillAsync(scenario.TenantId);

        var after = await ReadSoftDeletedAsync(_scraps, scenario.ScrapId);
        after.CompanyId.ShouldBe(scenario.CompanyId);

        // SİLİNMİŞ KALMALI: backfill kaydı DİRİLTMEZ ve kullanıcının gerçek silme anını YENİDEN damgalamaz.
        // (ABP'de soft-delete'li entity'yi UpdateAsync etmek silme yolunu tetikler — bu testin asıl işi bu
        // davranışı varsaymak yerine MEKANİK olarak bağlamaktır; kod incelemesi bulgusu.)
        after.IsDeleted.ShouldBeTrue();
        after.DeletionTime.ShouldBe(deletionTimeBefore);
    }

    /// <summary>Backfill GERÇEK bir working company altında da çalışmalı — <c>Disable&lt;ICompanyScoped&gt;()</c>
    /// olmasaydı yetim satırlar (CompanyId=Guid.Empty) süzülür ve backfill SESSİZ no-op olurdu. Diğer testler
    /// working company'yi null'a çektiği için bu kolu hiç denemiyordu (kod incelemesi bulgusu).</summary>
    [Fact]
    public async Task Backfills_even_when_a_real_working_company_is_active()
    {
        var scenario = await SeedAllFamiliesAsync("B7W");

        await ForceEmptyCompanyAsync(scenario);

        // Konsolide (null) DEĞİL: gerçek bir şirket seçili — filtre yetimleri gizlemeye çalışır.
        _companyContext.CompanyId = scenario.CompanyId;
        using (_currentTenant.Change(scenario.TenantId))
        {
            await WithUnitOfWorkAsync(() => _backfiller.BackfillAllTenantsAsync());
        }

        await AssertAllOwnedByAsync(scenario, scenario.CompanyId);
    }

    // ── kurulum / yardımcılar ────────────────────────────────────────────────

    private Task RunBackfillAsync(Guid tenantId)
    {
        _companyContext.CompanyId = null;

        using (_currentTenant.Change(tenantId))
        {
            return WithUnitOfWorkAsync(() => _backfiller.BackfillAllTenantsAsync());
        }
    }

    private async Task AssertAllOwnedByAsync(FamilyScenario s, Guid expectedCompanyId)
    {
        (await ReadAcrossFiltersAsync(_metals, s.MetalId)).CompanyId.ShouldBe(expectedCompanyId);
        (await ReadAcrossFiltersAsync(_stones, s.StoneId)).CompanyId.ShouldBe(expectedCompanyId);
        (await ReadAcrossFiltersAsync(_jewelries, s.JewelryId)).CompanyId.ShouldBe(expectedCompanyId);
        (await ReadAcrossFiltersAsync(_goods, s.GoodId)).CompanyId.ShouldBe(expectedCompanyId);
        (await ReadAcrossFiltersAsync(_scraps, s.ScrapId)).CompanyId.ShouldBe(expectedCompanyId);
        (await ReadAcrossFiltersAsync(_futures, s.FutureId)).CompanyId.ShouldBe(expectedCompanyId);
        (await ReadAcrossFiltersAsync(_services, s.ServiceId)).CompanyId.ShouldBe(expectedCompanyId);
    }

    /// <summary>Tenant + company filtreleri kapalı okuma — yetim (Guid.Empty) satır da görünsün.</summary>
    private Task<TEntity> ReadAcrossFiltersAsync<TEntity>(IRepository<TEntity, Guid> repository, Guid id)
        where TEntity : class, IEntity<Guid>
    {
        return WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<IMultiTenant>())
            using (_dataFilter.Disable<ICompanyScoped>())
            {
                return await repository.GetAsync(id);
            }
        });
    }

    private Task<TEntity> ReadSoftDeletedAsync<TEntity>(IRepository<TEntity, Guid> repository, Guid id)
        where TEntity : class, IEntity<Guid>
    {
        return WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<IMultiTenant>())
            using (_dataFilter.Disable<ICompanyScoped>())
            using (_dataFilter.Disable<ISoftDelete>())
            {
                return await repository.GetAsync(id);
            }
        });
    }

    /// <summary>Her aileden bir satır + org grafı kurar. Kodlar yine de benzersizleştirilir: aynı test
    /// metodunda birden fazla senaryo kurulabiliyor (pozitif kontroller) ve prefix tek başına yetmez.</summary>
    private async Task<FamilyScenario> SeedAllFamiliesAsync(string prefix)
    {
        // Working company null: emtia ailelerinde auto-stamp YOK (o yalnız ICompanyScoped içindir) — buradaki
        // işlevi OKUMA filtresini permissive yapmak, yani kurulum satırlarının sorgularda görünür kalması.
        _companyContext.CompanyId = null;

        var tenantId = SimpleGuidGenerator.Instance.Create();
        var suffix   = SimpleGuidGenerator.Instance.Create().ToString("N")[..6].ToUpperInvariant();

        using (_currentTenant.Change(tenantId))
        {
            return await WithUnitOfWorkAsync(async () =>
            {
                var data = await _seeder.SeedCompanyGraphAsync(prefix);
                var companyId = data.CompanyId;

                var metal = await _metals.InsertAsync(
                    new Metal($"{prefix}MT{suffix}", $"{prefix} Metal", data.HasUnitId, companyId), autoSave: true);
                var stone = await _stones.InsertAsync(
                    new Stone($"{prefix}ST{suffix}", $"{prefix} Stone", companyId), autoSave: true);
                var jewelry = await _jewelries.InsertAsync(
                    new Jewelry($"{prefix}JW{suffix}", $"{prefix} Jewelry", companyId), autoSave: true);
                var good = await _goods.InsertAsync(
                    new Good($"{prefix}GD{suffix}", $"{prefix} Good", companyId), autoSave: true);
                var scrap = await _scraps.InsertAsync(
                    new Scrap($"{prefix}SC{suffix}", $"{prefix} Scrap", data.HasUnitId, companyId), autoSave: true);
                var future = await _futures.InsertAsync(
                    new Future($"{prefix}FT{suffix}", $"{prefix} Future", data.HasUnitId, companyId), autoSave: true);

                var serviceCode = $"{prefix}SV{suffix}";
                var service = await _services.InsertAsync(
                    new Service(serviceCode, $"{prefix} Service", companyId), autoSave: true);

                return new FamilyScenario(
                    tenantId, companyId, data.HasUnitId,
                    metal.Id, stone.Id, jewelry.Id, good.Id, scrap.Id, future.Id, service.Id,
                    service.Code);
            });
        }
    }

    /// <summary>Migration'ın bıraktığı yetim durumunu ham SQL ile simüle eder (entity ctor'u Guid.Empty
    /// sahipliğe izin vermediği için SQL şart; change-tracker da bypass edilir).
    /// <para><b>Kurulumun ETKİ ETTİĞİ doğrulanır</b> (kod incelemesi bulgusu): tablo adı ya da filtre yanlış
    /// olsaydı UPDATE 0 satır etkiler, backfill hiçbir şey yapmasa da testler yeşil kalırdı — yani "yeşil ama
    /// hiçbir şey kanıtlamayan" test. Artık yetim durumu OKUNARAK teyit edilir.</para></summary>
    private async Task ForceEmptyCompanyAsync(FamilyScenario s)
    {
        await ExecuteRawAsync("UPDATE AppMetals SET CompanyId = {0} WHERE Id = {1}", Guid.Empty, s.MetalId);
        await ExecuteRawAsync("UPDATE AppStones SET CompanyId = {0} WHERE Id = {1}", Guid.Empty, s.StoneId);
        await ExecuteRawAsync("UPDATE AppJewelries SET CompanyId = {0} WHERE Id = {1}", Guid.Empty, s.JewelryId);
        await ExecuteRawAsync("UPDATE AppGoods SET CompanyId = {0} WHERE Id = {1}", Guid.Empty, s.GoodId);
        await ExecuteRawAsync("UPDATE AppScraps SET CompanyId = {0} WHERE Id = {1}", Guid.Empty, s.ScrapId);
        await ExecuteRawAsync("UPDATE AppFutures SET CompanyId = {0} WHERE Id = {1}", Guid.Empty, s.FutureId);
        await ExecuteRawAsync("UPDATE AppServices SET CompanyId = {0} WHERE Id = {1}", Guid.Empty, s.ServiceId);

        await AssertAllOwnedByAsync(s, Guid.Empty);   // ÖN KOŞUL: gerçekten yetim durumdayız
    }

    private Task ExecuteRawAsync(string sql, params object[] parameters)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var db = await _dbContextProvider.GetDbContextAsync();
            await db.Database.ExecuteSqlRawAsync(sql, parameters);
        });
    }

    private sealed record FamilyScenario(
        Guid TenantId,
        Guid CompanyId,
        Guid UnitId,
        Guid MetalId,
        Guid StoneId,
        Guid JewelryId,
        Guid GoodId,
        Guid ScrapId,
        Guid FutureId,
        Guid ServiceId,
        string ServiceCode);
}
