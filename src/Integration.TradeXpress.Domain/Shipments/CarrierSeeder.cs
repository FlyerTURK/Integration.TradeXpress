using Microsoft.Extensions.Logging;
using Integration.TradeXpress.N11Shipments;

namespace Integration.TradeXpress.Shipments;

/// <summary>
/// Çekirdek kargo firması (<see cref="Carrier"/>) seed'i / eşlemesi — host-global, idempotent. N11 kargo
/// firmalarından (<see cref="N11ShipmentCompany"/>) çekirdek Carrier kataloğunu türetir: her firma için
/// <c>Code = normalize(ShortName)</c> anahtarıyla Carrier upsert eder, sonra firmanın çekirdek köprüsünü
/// (<see cref="N11ShipmentCompany.CoreCarrierId"/>) doldurur.
/// <para>N11 firma verisi BOŞSA (DbMigrator seed'i N11 sync'ten önce koşabilir) türetme ATLANIR ve loglanır —
/// N11 sync sonrası tekrar çalışınca dolar (idempotent). Host bağlamı <see cref="ICurrentTenant.Change(Guid?)"/>
/// (null) ile garanti edilir.</para>
/// <para>GeographySeeder ikizi. Aynı reconcile <see cref="SeedAsync"/> ile <c>N11ReferenceSyncWorker</c>
/// firma re-sync'inden sonra da çağrılır (çekirdek referans DB'de taze kalsın).</para>
/// </summary>
public class CarrierSeeder(
    IRepository<Carrier, Guid> carrierRepository,
    IRepository<N11ShipmentCompany, Guid> n11ShipmentCompanyRepository,
    ICurrentTenant currentTenant,
    IUnitOfWorkManager unitOfWorkManager,
    ILogger<CarrierSeeder> logger)
    : ITransientDependency
{
    #region Fields

    private readonly IRepository<Carrier, Guid> _carrierRepository = carrierRepository;
    private readonly IRepository<N11ShipmentCompany, Guid> _n11ShipmentCompanyRepository = n11ShipmentCompanyRepository;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager = unitOfWorkManager;
    private readonly ILogger<CarrierSeeder> _logger = logger;

    #endregion

    #region Seeding

    /// <summary>N11 kargo firmalarından çekirdek Carrier kataloğunu upsert eder + köprüyü doldurur (host-only,
    /// idempotent). Güncellenen/oluşturulan köprü sayısını döner (log/worker için). Yalnız host (TenantId=null)
    /// bağlamında çağrılmalı — Carrier/N11ShipmentCompany host-global (IMultiTenant değil).</summary>
    public async Task<int> SeedAsync()
    {
        // Carrier + N11ShipmentCompany host-global (TenantId yok). Change(null) host bağlamını garanti eder.
        using (_currentTenant.Change(null))
        {
            var companies = (await _n11ShipmentCompanyRepository.GetQueryableAsync()).ToList();
            if (companies.Count == 0)
            {
                _logger.LogInformation(
                    "Kargo seed: N11 firma verisi boş — çekirdek Carrier türetme atlandı (N11 sync sonrası tekrar çalışınca dolar).");
                return 0;
            }

            var carrierByCode = new Dictionary<string, Carrier>(StringComparer.OrdinalIgnoreCase);
            foreach (var carrier in await GetAllCarriers())
            {
                carrierByCode[carrier.Code] = carrier;
            }

            var addedCarriers = 0;
            var linked = 0;
            var nameDerived = 0;
            foreach (var company in companies)
            {
                // Çekirdek kod kaynağı: N11 kısa kodu (ShortName). N11 bazı firmaları kısa-kodSUZ döndürür
                // (ör. DHL/Asil/Fillo Kargo — ShortName boş) → çekirdek kodu firma adından (Name) türet ki firma
                // yine çekirdeğe katılsın (atlama YOK). TÜRETME YALNIZ bizim Carrier.Code'undadır; N11'in wire
                // verisi (<see cref="N11ShipmentCompany.ShortName"/>) DEĞİŞTİRİLMEZ — boş kalır (N11 gerçeği korunur).
                var shortName = company.ShortName?.Trim();
                var codeSource = string.IsNullOrEmpty(shortName) ? company.Name : shortName;
                if (string.IsNullOrEmpty(shortName))
                {
                    nameDerived++;
                }

                // Anahtar = entity'nin ürettiği normalize koddur → sözlük araması ctor'un yazacağı Code ile birebir eşleşir.
                var code = NormalizeCarrierCode(codeSource);
                if (carrierByCode.TryGetValue(code, out var carrier) == false)
                {
                    carrier = new Carrier(codeSource, company.Name);
                    await _carrierRepository.InsertAsync(carrier, autoSave: false);
                    carrierByCode[carrier.Code] = carrier;
                    addedCarriers++;
                }

                // N11 köprüsü (idempotent — zaten doğruysa dokunma).
                if (company.CoreCarrierId != carrier.Id)
                {
                    company.SetCoreCarrier(carrier.Id);
                    await _n11ShipmentCompanyRepository.UpdateAsync(company, autoSave: false);
                    linked++;
                }
            }

            await SaveAsync();
            _logger.LogInformation(
                "Kargo seed: {Added} çekirdek Carrier eklendi, {Linked} N11 firma köprüsü güncellendi, {NameDerived} firma kodu Name'den türedi (kaynak {Total} firma).",
                addedCarriers, linked, nameDerived, companies.Count);
            return linked;
        }
    }

    #endregion

    #region Helpers

    // Carrier upsert anahtarı — N11 ShortName'i entity'nin normalize kuralıyla (kültür-bağımsız UPPER) aynılaştırır,
    // ki sözlük araması Carrier ctor'unun ürettiği Code ile birebir eşleşsin (drift/çift-kayıt olmasın).
    private static string NormalizeCarrierCode(string shortName)
    {
        return StringFieldGuard.NormalizeInvariantCode(shortName, nameof(Carrier.Code), 1, CarrierConsts.CodeMaxLength);
    }

    private async Task<List<Carrier>> GetAllCarriers()
    {
        return (await _carrierRepository.GetQueryableAsync()).ToList();
    }

    // Bekleyen tüm değişiklikleri tek seferde veritabanına yazar (GeographySeeder deseniyle hizalı).
    private async Task SaveAsync()
    {
        await _unitOfWorkManager.Current!.SaveChangesAsync();
    }

    #endregion
}
