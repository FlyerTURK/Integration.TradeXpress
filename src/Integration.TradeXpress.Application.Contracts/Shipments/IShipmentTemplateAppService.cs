using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Shipments;

/// <summary>Birleşik ERP kargo şablonu CRUD — <b>company-owned</b> katalog (kanal-nötr çekirdek). Standart kimlik
/// (Code company-scoped benzersiz) + menşei/iade adresi + ücret modeli + hazırlık/teslim süresi. Ürün formu lookup'ı
/// için <see cref="GetPickerListAsync"/>. Silme, referans veren ürün varsa engellenir (silme-guard).</summary>
public interface IShipmentTemplateAppService : ICrudAppService<
    ShipmentTemplateGetDto,
    ShipmentTemplateListDto,
    System.Guid,
    ShipmentTemplateListRequestDto,
    ShipmentTemplateCreateDto,
    ShipmentTemplateUpdateDto>
{
    /// <summary>Combo/picker (Product formu kargo şablonu ataması) — aktif şablonlar, Name sıralı.</summary>
    Task<List<ShipmentTemplateListDto>> GetPickerListAsync();
}
