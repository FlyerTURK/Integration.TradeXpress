using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Branches;

public interface IBranchAppService : ICrudAppService<
    BranchGetDto,
    BranchListDto,
    Guid,
    BranchListRequestDto,
    BranchCreateDto,
    BranchUpdateDto>
{
    /// <summary>
    /// Working-context şube seçici için kullanıcının ERİŞEBİLDİĞİ şubeler (server-side kapsam daraltması;
    /// <see cref="Authorization.IScopedGrantResolver"/> ile). Client'a güvenilmez — filtre sunucuda uygulanır.
    /// </summary>
    Task<List<BranchListDto>> GetMyBranchesAsync();
}
