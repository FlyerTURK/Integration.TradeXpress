using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization;
using Volo.Abp.Security.Claims;

namespace Integration.TradeXpress.Security;

/// <summary>
/// Testten yönetilebilir sahte yetki servisi: varsayılan HER ŞEYE İZİN (always-allow ile aynı davranış),
/// yalnız <see cref="DeniedPolicies"/>'e eklenen policy'ler reddedilir. Böylece per-tip işlem yetkisi
/// (<c>ProcessTypePermissionMap</c> → <c>AuthorizationService.CheckAsync</c>) izin ver/verme
/// ekseninde test edilebilir. Bilinçli olarak DI marker'ı YOK — yalnız
/// <c>VoucherPermissionTestModule</c> kendi startup grafında elle kaydeder; diğer test sınıfları
/// test tabanındaki always-allow ile değişmeden çalışır.
/// </summary>
public class DeniableAuthorizationService : IAbpAuthorizationService
{
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    public DeniableAuthorizationService(
        ICurrentPrincipalAccessor principalAccessor,
        IServiceProvider serviceProvider)
    {
        _principalAccessor = principalAccessor;
        ServiceProvider = serviceProvider;
    }

    public IServiceProvider ServiceProvider { get; }

    public ClaimsPrincipal CurrentPrincipal
    {
        get { return _principalAccessor.Principal; }
    }

    /// <summary>Reddedilecek policy (izin) adları — test doldurur/temizler.</summary>
    public ISet<string> DeniedPolicies { get; } = new HashSet<string>();

    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
    {
        // Requirement-bazlı kontroller (class-level [Authorize] vb.) daima geçer — odak policy adlarıdır.
        return Task.FromResult(AuthorizationResult.Success());
    }

    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user, object? resource, string policyName)
    {
        return Task.FromResult(DeniedPolicies.Contains(policyName)
            ? AuthorizationResult.Failed()
            : AuthorizationResult.Success());
    }
}
