using System;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Addressing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Shipments;

/// <summary>
/// <see cref="IShipmentTemplateReconciler"/> uygulaması — çekirdek <see cref="ShipmentTemplate"/> deposunu (company-owned +
/// per-tenant) doğrudan kullanır; company-scope'u ELLE uygular (find-or-create). Kod eşleşmesi
/// <see cref="ShipmentTemplateAppService"/>'in <c>EnsureCodeUniqueAsync</c> company-scoped kuralıyla BİREBİR hizalıdır
/// (aynı (TenantId, CompanyId, Code) benzersizliği). Ters-üretilen çekirdek KISMÎ/TASLAK'tır — kullanıcı sonra
/// çekirdek formdan zenginleştirir (canlı miras / otomatik ezme YOK).
/// </summary>
public class ShipmentTemplateReconciler : IShipmentTemplateReconciler, ITransientDependency
{
    private readonly IRepository<ShipmentTemplate, Guid> _repository;

    public ShipmentTemplateReconciler(IRepository<ShipmentTemplate, Guid> repository)
    {
        _repository = repository;
    }

    public virtual async Task<Guid> FindOrCreateFromChannelAsync(Guid companyId, string templateName, Address warehouseAddress)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(companyId));
        }

        if (warehouseAddress is null)
        {
            throw new RequiredPropertyException(nameof(warehouseAddress));
        }

        // Çekirdek Code = kanal şablon adının normalize hâli (çekirdek Code kuralıyla BİREBİR: min/max + UPPER + boşluk korunur).
        var code = StringFieldGuard.NormalizeCode(
            templateName, nameof(ShipmentTemplate.Code), EntityFieldConsts.CodeMinLength, ShipmentTemplateConsts.CodeMaxLength);

        // Idempotent: aynı şirkette aynı kodlu çekirdek varsa YENİDEN oluşturma — mevcudu bağla (origin-guard çağırandadır).
        var existing = await _repository.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Code == code);
        if (existing is not null)
        {
            return existing.Id;
        }

        // Depo adresi → gönderim (ÖZEL) adresi (şube YOK); hazırlık günü (1,1). Ad = ham TemplateName (SetName TitleCase uygular).
        var created = new ShipmentTemplate(
            companyId,
            code,
            templateName,
            dispatchBranchId: null,
            dispatchAddress: CloneAddress(warehouseAddress),
            processingDaysMin: 1,
            processingDaysMax: 1);
        await _repository.InsertAsync(created, autoSave: true);
        return created.Id;
    }

    // Depo adresi VO'sunu birebir KOPYALAR — aynı owned-VO örneğini iki farklı sahibe (N11 WarehouseAddress +
    // çekirdek DispatchAddress) paylaştırıp EF owned-tracking'i bozmamak için taze örnek üretir (değer eşitliği korunur).
    private static Address CloneAddress(Address source)
    {
        return new Address(
            source.City,
            source.Line,
            source.District,
            source.Neighborhood,
            source.PostalCode,
            source.CountryCode,
            source.Title,
            source.CityCode,
            source.DistrictCode,
            source.AdministrativeAreaId,
            source.LocalityId,
            source.AdministrativeAreaIsoCode,
            source.BuildingName,
            source.BuildingNumber,
            source.Room,
            source.Floor,
            source.Postbox,
            source.AdditionalStreetName);
    }
}
