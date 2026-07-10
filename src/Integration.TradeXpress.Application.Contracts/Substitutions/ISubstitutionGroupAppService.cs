using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil grubu CRUD — company-owned tanım (working company'ye scope'lu; ICompanyOwned query-filter).
/// Emtia satırları grafı Create/Update input'unun İÇİNDE taşınır (Account→SubAccount drill deseni):
/// Id boş → ekle, IsDeleted → sil, aksi → güncelle; DisplayOrder = tüketim önceliği KORUNUR.
/// </summary>
public interface ISubstitutionGroupAppService : ICrudAppService<
    SubstitutionGroupGetDto,
    SubstitutionGroupListDto,
    Guid,
    SubstitutionGroupListRequestDto,
    SubstitutionGroupCreateDto,
    SubstitutionGroupUpdateDto>
{
}
