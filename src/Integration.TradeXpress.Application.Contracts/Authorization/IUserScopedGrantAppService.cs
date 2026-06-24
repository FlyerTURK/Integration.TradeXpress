using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Authorization;

/// <summary>Kullanıcıya scoped rol/izin atama (Faz 1: depolama/listeleme). Çözümleme Faz 2'de.</summary>
public interface IUserScopedGrantAppService : IApplicationService
{
    Task<List<UserScopedGrantDto>> GetByUserAsync(Guid userId);
    Task<UserScopedGrantDto> CreateAsync(UserScopedGrantCreateDto input);
    Task DeleteAsync(Guid id);
}
