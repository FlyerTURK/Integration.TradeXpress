using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Stones;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Company global query filter regresyon ağı — emtia aileleri artık <see cref="ICompanyOwned"/> (GÜVENLİK SINIRI;
/// örnek: Stone). DbContext seviyesindeki ABP data filter'ının, elle çağrılan
/// <c>CompanyScopedQueryable.WhereCompanyOwnedVisible</c> ile BİREBİR aynı görünürlüğü — unutulması imkânsız
/// şekilde — verdiğini doğrular. Konsolide/rapor sorguları <c>DataFilter.Disable&lt;ICompanyScoped&gt;()</c> ile
/// bilinçli açar (filtre anahtarı her iki marker için de ICompanyScoped'tır).
///
/// <para><b>Model değişimi (görev #4):</b> eski üç katman (host / holding-CompanyId=null / şirkete-özel) YERİNE
/// tek katman: her emtia bir şirkete AİTTİR. "Holding" (sahipsiz) kayıt artık üretilemez — CompanyId zorunlu —
/// ve bu testin eski "shared/holding görünür" iddiaları bilinçli olarak KALDIRILDI: o katman, bir şirketin
/// kullanıcısının kardeş şirketleri etkileyebilmesinin (cross-company manipülasyon) taşıyıcısıydı.
/// KIRMIZIYSA sızıntı geri gelmiş demektir — testi gevşetme, kök nedeni düzelt.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class CompanyScopedFilterTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IRepository<Stone, Guid> _stones;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;

    public CompanyScopedFilterTests()
    {
        _stones         = GetRequiredService<IRepository<Stone, Guid>>();
        _dataFilter     = GetRequiredService<IDataFilter>();
        _currentTenant  = GetRequiredService<ICurrentTenant>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task Filter_hides_other_company_records_when_working_company_is_set()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.CompanyA;

            var visible = await QueryIdsAsync(data);

            // Yalnız KENDİ şirketinin kaydı görünür; diğer şirketinki GÖRÜNMEZ.
            visible.ShouldContain(data.StoneA);
            visible.ShouldNotContain(data.StoneB);
        }
    }

    /// <summary>
    /// Geçiş backfill'inin kapsadığı 7 EMTİA ailesi — <c>CompanyOwnedBackfiller.BackfillCommoditiesAsync</c>
    /// listesiyle birebir aynı olmalıdır.
    ///
    /// <para><b>Neden reflection DEĞİL:</b> <see cref="ICompanyOwned"/>'ı Voucher/Product/SalesChannel dahil 30+
    /// tip uygular; bunlar CompanyId ile DOĞAR, geçiş backfill'ine ihtiyaçları yoktur. Assembly taraması denendi
    /// ve geri alındı — "emtia" tip sisteminden türetilemiyor. Dolayısıyla bu liste ELLEdir ve şu boşluk AÇIK
    /// kalır: yeni bir emtia ailesi eklenip HEM buraya HEM backfiller'a yazılmazsa sessizce kapsam dışı kalır.
    /// Kapatmanın tek dürüst yolu bir emtia marker'ı (ör. <c>ICommodity</c>) tanımlamaktır — ayrı karar.</para>
    /// </summary>
    public static TheoryData<Type> CommodityTypes
    {
        get
        {
            return new TheoryData<Type>
            {
                typeof(Metals.Metal), typeof(Stone), typeof(Jewelries.Jewelry), typeof(Goods.Good),
                typeof(Scraps.Scrap), typeof(Futures.Future), typeof(Services.Service),
            };
        }
    }

    /// <summary>Sahipsiz ("holding") kayıt ÜRETİLEMEZ — CompanyId zorunludur. Bu, cross-company
    /// manipülasyonun taşıyıcı katmanının yapısal olarak kapandığının kanıtıdır. Bir ctor CompanyId'yi
    /// opsiyonelleştirirse bu test KIRMIZI olur — sızıntı sessiz geri gelemez.</summary>
    [Theory]
    [MemberData(nameof(CommodityTypes))]
    public void Ownerless_commodity_cannot_be_constructed(Type commodityType)
    {
        typeof(ICompanyOwned).IsAssignableFrom(commodityType).ShouldBeTrue();
        typeof(ICompanyScoped).IsAssignableFrom(commodityType).ShouldBeFalse();   // iki marker birbirini dışlar

        // Derleme-zamanı garantisi: HER public ctor'da companyId Guid? DEĞİL Guid ve opsiyonel değil.
        var publicCtors = commodityType
            .GetConstructors()
            .Where(c => c.GetParameters().Length > 0)
            .ToList();

        publicCtors.ShouldNotBeEmpty();

        foreach (var ctor in publicCtors)
        {
            var companyIdParameter = ctor.GetParameters().SingleOrDefault(p => p.Name == "companyId");

            companyIdParameter.ShouldNotBeNull($"{commodityType.Name} ctor'ı companyId ALMIYOR → sahipsiz kayıt mümkün");
            companyIdParameter.ParameterType.ShouldBe(typeof(Guid));   // Guid? olsaydı sahipsiz kayıt mümkün olurdu
            companyIdParameter.IsOptional.ShouldBeFalse();
        }
    }

    /// <summary>Her sahipli entity geçiş backfill'ini DESTEKLEMELİ: migration <c>CompanyId</c>'yi Guid.Empty
    /// bıraktığında <c>CompanyOwnedBackfiller</c> bu metotla sahiplendirir. Yeni bir aile eklenip metot
    /// unutulursa backfill onu SESSİZCE atlar (derleme hatası vermez — delege çağrı yerinden geçiliyor)
    /// → bu test o boşluğu KIRMIZIYA çevirir.</summary>
    [Theory]
    [MemberData(nameof(CommodityTypes))]
    public void Every_commodity_family_supports_company_backfill(Type commodityType)
    {
        var method = commodityType.GetMethod(
            nameof(Stone.BackfillCompanyIfMissing), new[] { typeof(Guid) });

        method.ShouldNotBeNull($"{commodityType.Name}.BackfillCompanyIfMissing(Guid) YOK → backfill bunu atlar");
        method.IsPublic.ShouldBeTrue();
        method.IsVirtual.ShouldBeTrue();   // EF/ABP proxy gereği (entity-conventions)
    }

    /// <summary>Şekil denetimi YETMEZ (kod incelemesi bulgusu): metot var ama gövdesi yanlışsa test yeşil kalırdı.
    /// Burada DAVRANIŞ çalıştırılır — 3 kural: (1) boşsa doldurur, (2) doluysa DOKUNMAZ (set-once),
    /// (3) Guid.Empty parametreyi REDDEDER. 7 kopya gövdenin sessizce sapması artık mümkün değil.</summary>
    [Theory]
    [MemberData(nameof(CommodityTypes))]
    public void Company_backfill_behaviour_is_identical_across_families(Type commodityType)
    {
        var method = commodityType.GetMethod(
            nameof(Stone.BackfillCompanyIfMissing), new[] { typeof(Guid) })!;

        var companyIdProperty = commodityType.GetProperty(nameof(ICompanyOwned.CompanyId))!;
        var first  = SimpleGuidGenerator.Instance.Create();
        var second = SimpleGuidGenerator.Instance.Create();

        // EF'in kullandığı parametresiz (protected) ctor ile örnek üret — iş kuralı ctor'ını atlar.
        var instance = Activator.CreateInstance(commodityType, nonPublic: true)!;

        // (1) boşken doldurur
        method.Invoke(instance, new object[] { first });
        companyIdProperty.GetValue(instance).ShouldBe(first, $"{commodityType.Name}: boş CompanyId doldurulmadı");

        // (2) doluyken DOKUNMAZ (idempotent no-op; sahiplik yeniden atanamaz)
        method.Invoke(instance, new object[] { second });
        companyIdProperty.GetValue(instance).ShouldBe(first, $"{commodityType.Name}: sahiplik YENİDEN atandı");

        // (3) Guid.Empty reddedilir (fail-fast) — sahte sahiplik damgası oluşmaz
        var fresh = Activator.CreateInstance(commodityType, nonPublic: true)!;
        var ex = Should.Throw<TargetInvocationException>(
            () => method.Invoke(fresh, new object[] { Guid.Empty }));
        ex.InnerException.ShouldBeOfType<RequiredPropertyException>(
            $"{commodityType.Name}: Guid.Empty sahiplik reddedilmedi");
    }

    [Fact]
    public async Task Disable_filter_shows_all_companies_for_consolidated_queries()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.CompanyA;

            using (_dataFilter.Disable<ICompanyScoped>())
            {
                var visible = await QueryIdsAsync(data);

                // Konsolide sorgu: tenant'ın TÜM şirketleri görünür (tenant filtresi hâlâ açık).
                visible.ShouldContain(data.StoneA);
                visible.ShouldContain(data.StoneB);
            }
        }
    }

    [Fact]
    public async Task Filter_matches_WhereCompanyOwnedVisible_semantics_exactly()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            // Working şirket dolu VE boş (konsolide) — iki durumda da parite birebir olmalı.
            foreach (var workingCompanyId in new Guid?[] { data.CompanyA, null })
            {
                _companyContext.CompanyId = workingCompanyId;

                List<Guid> filtered;
                using (_dataFilter.Disable<IMultiTenant>())
                {
                    filtered = await QueryIdsAsync(data);
                }

                List<Guid> manual;
                using (_dataFilter.Disable<IMultiTenant>())
                using (_dataFilter.Disable<ICompanyScoped>())
                {
                    manual = await WithUnitOfWorkAsync(async () =>
                    {
                        var stones = await _stones.GetListAsync(s => data.AllIds.Contains(s.Id));
                        return stones
                            .AsQueryable()
                            .WhereCompanyOwnedVisible(data.TenantId, workingCompanyId)
                            .Select(s => s.Id)
                            .ToList();
                    });
                }

                filtered.OrderBy(x => x).ShouldBe(manual.OrderBy(x => x));
            }
        }
    }

    /// <summary>Tek tenant altında İKİ ŞİRKETE ait taş kurar. Artık "tenant-geneli/holding" katmanı YOK —
    /// her kayıt bir şirkete aittir. Kodlar test-başına benzersiz (paylaşılan Sqlite collection DB'si).</summary>
    private async Task<FilterTestData> SeedAsync()
    {
        _companyContext.CompanyId = null; // auto-stamp devre dışı — CompanyId ctor'dan verilir

        var tenantId = SimpleGuidGenerator.Instance.Create();
        var companyA = SimpleGuidGenerator.Instance.Create();
        var companyB = SimpleGuidGenerator.Instance.Create();
        var suffix   = SimpleGuidGenerator.Instance.Create().ToString("N")[..8].ToUpperInvariant();

        Guid stoneA, stoneB;
        using (_currentTenant.Change(tenantId))
        {
            (stoneA, stoneB) = await WithUnitOfWorkAsync(async () =>
            {
                var a = await _stones.InsertAsync(new Stone($"A{suffix}", $"Company A Stone {suffix}", companyA));
                var b = await _stones.InsertAsync(new Stone($"B{suffix}", $"Company B Stone {suffix}", companyB));
                return (a.Id, b.Id);
            });
        }

        return new FilterTestData(tenantId, companyA, companyB, stoneA, stoneB);
    }

    /// <summary>Bu testin tohumladığı taşlardan aktif filtrelerle görünenlerin id listesi.</summary>
    private Task<List<Guid>> QueryIdsAsync(FilterTestData data)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var stones = await _stones.GetListAsync(s => data.AllIds.Contains(s.Id));
            return stones.Select(s => s.Id).ToList();
        });
    }

    private sealed record FilterTestData(Guid TenantId, Guid CompanyA, Guid CompanyB, Guid StoneA, Guid StoneB)
    {
        public IReadOnlyList<Guid> AllIds { get; } = new[] { StoneA, StoneB };
    }
}
