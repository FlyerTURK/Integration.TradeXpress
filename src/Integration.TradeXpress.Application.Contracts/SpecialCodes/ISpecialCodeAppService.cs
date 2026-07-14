using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.SpecialCodes;

public interface ISpecialCodeAppService : ICrudAppService<
    SpecialCodeGetDto,
    SpecialCodeListDto,
    Guid,
    SpecialCodeListRequestDto,
    SpecialCodeCreateDto,
    SpecialCodeUpdateDto>
{
    /// <summary>Bir (entity, property) bağlamının AKTİF özel kodları — picker combo'sunun kaynağı. Host/holding-host
    /// (CompanyId null) + verilen/çalışılan şirkete-özel kayıtlar; koda göre sıralı.</summary>
    Task<List<SpecialCodeListDto>> GetForContextAsync(string entityName, string propertyName, Guid? companyId = null);
}
