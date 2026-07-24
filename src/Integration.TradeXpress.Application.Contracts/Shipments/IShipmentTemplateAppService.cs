using System;
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

    /// <summary>Çekirdek şablonun TÜM satış kanalı dağıtımlarını (deployment) döner — çekirdek formundaki "Satış Kanalları"
    /// drill'i tüketir. Kanal-agnostik: çekirdeğe K1 köprüsüyle (<c>{Kanal}ShipmentTemplate.ShipmentTemplateId</c>) bağlı
    /// her kanal-şablonu bir satır olur (şu an yalnız N11); kanal adı server'da çözülür. Çekirdek şablon çalışılan şirkete
    /// ait değilse (ya da yoksa) boş liste (GetList deseni).</summary>
    /// <param name="shipmentTemplateId">Dağıtımları listelenecek çekirdek kargo şablonunun id'si.</param>
    Task<List<ShipmentTemplateChannelDeploymentDto>> GetChannelDeploymentsAsync(Guid shipmentTemplateId);
}
