using Microsoft.Extensions.Logging;

namespace Integration.TradeXpress.Countries;

/// <summary>
/// Country id-only geçiş backfill'i — string-kod referanslarından yeni Guid kolonları doldurur:
/// <c>Company.CountryCode → Company.CountryId</c> ve <c>Country.DefaultCurrencyCode → Country.DefaultCurrencyUnitId</c>.
///
/// <para><b>Neden migration DIŞINDA:</b> kod→id eşleştirmesi veri okuma ister; migration SQL'i elle
/// yazılamaz (governance guard'ı Migrations düzenlemesini bloklar). <see cref="MultiCompany.CompanyOwnedBackfiller"/>
/// deseniyle hizalı: DbMigrator'ın migrate'ten hemen sonra çalıştırdığı seed akışında koşar.</para>
///
/// <para><b>İdempotent:</b> yalnız id'si boş (null) VE kodu dolu satırlara dokunur; ikinci koşuda no-op.
/// WHERE'siz toplu yazma YOK (satır satır, entity üzerinden). Eşleşmeyen kod SİLİNMEZ — uyarı loglanır
/// (kod alanı düzenlenebilir olduğundan öksüz kod insan kararı ister).</para>
///
/// <para><b>TÜM tenant'ları kapsar:</b> <see cref="IMultiTenant"/> filtresi Disable — host koşusunda BİR KEZ
/// çağrılır, tüm tenant'ların satırları dolar. <b>Host‖tenant çapraz görünürlük:</b> Country ve CurrencyUnit
/// kayıtları host-global (TenantId=null) ya da tenant'a ait olabilir; eşleştirmede satırın SAHİBİ tenant'ın
/// kendi kaydı ÖNCELİKLİ, yoksa host-global kayda düşülür (null‖own görünürlük kuralıyla aynı).</para>
/// </summary>
public class CountryReferenceBackfiller : DomainService
{
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<Country, Guid> _countryRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository;
    private readonly IDataFilter _dataFilter;

    public CountryReferenceBackfiller(
        IRepository<Company, Guid> companyRepository,
        IRepository<Country, Guid> countryRepository,
        IRepository<CurrencyUnit, Guid> currencyUnitRepository,
        IDataFilter dataFilter)
    {
        _companyRepository      = companyRepository;
        _countryRepository      = countryRepository;
        _currencyUnitRepository = currencyUnitRepository;
        _dataFilter             = dataFilter;
    }

    /// <summary>Sistemdeki (tüm tenant'lar) boş id'li Country/Company referanslarını string kodlarından
    /// doldurur. Boş satır yoksa (temiz kurulum ya da ikinci koşu) ucuz no-op.</summary>
    public async Task BackfillAllTenantsAsync()
    {
        // Tenant filtresi kapalı: tüm tenant'ların boş kayıtları tek koşuda görülür ve doldurulur.
        using (_dataFilter.Disable<IMultiTenant>())
        {
            // Önce Country.DefaultCurrencyUnitId (Company backfill'i Country'nin id'sini değil kodunu kullanır;
            // sıra bağımlılığı yok ama ülkelerin tam olması okunurluk açısından önde).
            await BackfillCountryDefaultCurrenciesAsync();
            await BackfillCompanyCountriesAsync();
        }
    }

    // Country.DefaultCurrencyCode → DefaultCurrencyUnitId (sahibi tenant'ın birimi öncelikli, yoksa host).
    private async Task BackfillCountryDefaultCurrenciesAsync()
    {
        // CS0618 (obsolete kod alanı okuma) BİLİNÇLİ: bu sınıf geçiş backfill'inin kendisidir.
        var orphans = await AsyncExecuter.ToListAsync(
            (await _countryRepository.GetQueryableAsync())
                .Where(c => c.DefaultCurrencyUnitId == null
                            && c.DefaultCurrencyCode != null
                            && c.DefaultCurrencyCode != ""));
        if (orphans.Count == 0)
        {
            return;
        }

        var units = await AsyncExecuter.ToListAsync(
            (await _currencyUnitRepository.GetQueryableAsync())
                .Select(u => new OwnedCode(u.Id, u.TenantId, u.Code)));

        var filled = 0;
        var unmatched = new List<string>();
        foreach (var country in orphans)
        {
            var unitId = ResolveOwnerFirst(units, country.DefaultCurrencyCode!, country.TenantId);
            if (unitId is { } id)
            {
                country.BackfillDefaultCurrencyUnitIfMissing(id);
                await _countryRepository.UpdateAsync(country, autoSave: true);
                filled++;
            }
            else
            {
                unmatched.Add($"{country.Code}→{country.DefaultCurrencyCode}");
            }
        }

        LogResult("Country.DefaultCurrencyUnitId", filled, unmatched);
    }

    // Company.CountryCode → CountryId (sahibi tenant'ın ülkesi öncelikli, yoksa host-global).
    private async Task BackfillCompanyCountriesAsync()
    {
        var orphans = await AsyncExecuter.ToListAsync(
            (await _companyRepository.GetQueryableAsync())
                .Where(c => c.CountryId == null
                            && c.CountryCode != null
                            && c.CountryCode != ""));
        if (orphans.Count == 0)
        {
            return;
        }

        var countries = await AsyncExecuter.ToListAsync(
            (await _countryRepository.GetQueryableAsync())
                .Select(c => new OwnedCode(c.Id, c.TenantId, c.Code)));

        var filled = 0;
        var unmatched = new List<string>();
        foreach (var company in orphans)
        {
            var countryId = ResolveOwnerFirst(countries, company.CountryCode!, company.TenantId);
            if (countryId is { } id)
            {
                company.BackfillCountryIfMissing(id);
                await _companyRepository.UpdateAsync(company, autoSave: true);
                filled++;
            }
            else
            {
                unmatched.Add($"{company.Code}→{company.CountryCode}");
            }
        }

        LogResult("Company.CountryId", filled, unmatched);
    }

    /// <summary>Kod→id çözümü (normalize: trim + harf-duyarsız): önce satır sahibinin KENDİ kaydı,
    /// yoksa host-global (TenantId=null) kayıt. Bulunamazsa null (satır dokunulmadan kalır).</summary>
    private static Guid? ResolveOwnerFirst(List<OwnedCode> candidates, string rawCode, Guid? ownerTenantId)
    {
        var code = rawCode.Trim();

        var own = candidates.FirstOrDefault(c =>
            c.TenantId == ownerTenantId && string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
        if (own is not null)
        {
            return own.Id;
        }

        var host = candidates.FirstOrDefault(c =>
            c.TenantId == null && string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
        return host?.Id;
    }

    // Sonuç raporu: kaç satır dolduruldu + eşleşmeyen kodlar (varsa UYARI — silinmez, insan kararı ister).
    private void LogResult(string target, int filled, List<string> unmatched)
    {
        Logger.LogInformation("Country id-only backfill [{Target}]: {Filled} satır dolduruldu.", target, filled);
        if (unmatched.Count > 0)
        {
            Logger.LogWarning(
                "Country id-only backfill [{Target}]: {Count} satır EŞLEŞMEDİ (kod→id bulunamadı): {Codes}",
                target, unmatched.Count, string.Join(", ", unmatched));
        }
    }

    // Eşleştirme adayı (id + sahibi + kod) — tek projeksiyonla çekilir.
    private sealed record OwnedCode(Guid Id, Guid? TenantId, string Code);
}
