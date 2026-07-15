namespace Integration.TradeXpress.Organization;

/// <summary>
/// Tenant org ağacı seed'i (tek sorumluluk): yeni tenant için varsayılan HQ şirket (Merkez/TR/TRY)
/// + en az bir HQ şube/kasa (mevcutların backfill'i dahil, <see cref="OrgTreeManager"/>). Yalnız tenant
/// (host'ta company yok). Base = global TRY; birimler henüz yoksa atlanır (sonraki run kurar).
/// Tekrar çalıştırılabilir.
/// </summary>
public class OrgSeeder(
    IRepository<Company, Guid> companyRepository,
    IRepository<CurrencyUnit, Guid> currencyUnitRepository,
    IRepository<Country, Guid> countryRepository,
    OrgTreeManager orgTree,
    IDataFilter dataFilter,
    ICurrentTenant currentTenant)
    : ITransientDependency
{
    #region Fields

    private readonly IRepository<Company, Guid> _companyRepository = companyRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository = currencyUnitRepository;
    private readonly IRepository<Country, Guid> _countryRepository = countryRepository;
    private readonly OrgTreeManager _orgTree = orgTree;
    private readonly IDataFilter _dataFilter = dataFilter;
    private readonly ICurrentTenant _currentTenant = currentTenant;

    #endregion

    #region Seeding

    /// <summary>Tenant'ın varsayılan HQ şirketini (yoksa) kurar; her şirkete HQ şube/kasa garanti eder.</summary>
    public async Task SeedHqCompanyAsync(Guid? tenantId)
    {
        using (_currentTenant.Change(tenantId))      // yazma bu tenant kapsamında olsun
        using (_dataFilter.Disable<IMultiTenant>())   // global TRY birimini görebilmek için
        {
            if (await TryEnsureHqCompany(tenantId) == false)
            {
                return; // base birim (TRY) henüz yok → backfill'i de atla, sonraki run kurar
            }

            await EnsureEachCompanyHasHqBranch(tenantId);
        }

        // HQ şirket yoksa kurar. Kurulabildiyse (zaten var ya da kuruldu) true; base birim yoksa false.
        async Task<bool> TryEnsureHqCompany(Guid? owner)
        {
            var hasHq = (await _companyRepository.GetQueryableAsync())
                .Any(c => c.TenantId == owner && c.IsHeadquarters);
            if (hasHq)
            {
                return true; // zaten var
            }

            var tryUnit = await GetHostTryUnit();
            if (tryUnit is null)
            {
                return false; // base birim yok → kuramayız
            }

            var trCountry = await GetHostTrCountry();
            if (trCountry is null)
            {
                return false; // ülke kataloğu (TR) henüz yok → kuramayız, sonraki run kurar
            }

            await _companyRepository.InsertAsync(BuildHqCompany(trCountry.Id, tryUnit.Id, owner), autoSave: true);
            return true;
        }

        // Her şirket en az bir HQ şube + varsayılan kasayla yaşamalı (mevcutların backfill'i dahil).
        async Task EnsureEachCompanyHasHqBranch(Guid? owner)
        {
            List<Company> companies = [.. (await _companyRepository.GetQueryableAsync())
                .Where(c => c.TenantId == owner)];

            foreach (var company in companies)
            {
                await _orgTree.EnsureHeadquartersBranchAsync(company);

                // NOT: kasa→kasa akışında CARİ HİÇ ÜRETİLMEZ (2026-07-15 ürün kararı) — kasa fişte doğrudan
                // karşı taraftır (Voucher.AccountType=Vault; Şube→AccountId, Kasa→SubAccountId). Seed yolunda
                // takas/kasa carisi kurulumu YOKTUR (eski şirket-geneli TRF-CLEARING modeli de emekli).
            }
        }

        // Global (host) Türk Lirası birimi — HQ'nun base para birimi.
        async Task<CurrencyUnit?> GetHostTryUnit()
        {
            return (await _currencyUnitRepository.GetQueryableAsync())
                .FirstOrDefault(u => u.TenantId == null && u.Code == CurrencyUnitCode.TRY);
        }

        // Global (host) Türkiye ülke kaydı — HQ'nun ülkesi (id-only referans; kod değil id yazılır).
        async Task<Country?> GetHostTrCountry()
        {
            return (await _countryRepository.GetQueryableAsync())
                .FirstOrDefault(c => c.TenantId == null && c.Code == "TR");
        }

        // Varsayılan merkez şirketi (Merkez / TR / base = TRY).
        static Company BuildHqCompany(Guid countryId, Guid baseCurrencyUnitId, Guid? owner)
        {
            return new Company(
                code: "MRK",
                name: "Merkez",
                countryId: countryId,
                baseCurrencyUnitId: baseCurrencyUnitId,
                isHeadquarters: true,
                displayOrder: 1);
        }
    }

    #endregion
}
