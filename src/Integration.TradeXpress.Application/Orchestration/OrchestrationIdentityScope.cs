using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// ORKESTRASYON KİMLİK KAPSAMI (2026-07-25 inceleme bulgusu #1 — zinciri kökten kıran hata):
/// arka plan job/handler'ları KİMLİKSİZ koşar; oysa zincirin çağırdığı app-service'ler
/// (<c>SubstitutionCalculationAppService</c> [Authorize(Substitutions.Default)],
/// <c>SalesChannelTrN11ProductAppService.SyncStockAndPriceAsync</c> [Authorize(SalesChannels.Update)])
/// yetki ister → kimliksiz bağlamda AbpAuthorizationException: muadil job'ı her tetikte patlıyor,
/// push ise pusher'ın catch'inde SESSİZCE yutuluyordu — kanala hiçbir şey gitmiyordu.
/// <para><b>Çözüm:</b> tenant'ın admin kullanıcısını impersonate et — TenantAppService'in kanıtlanmış
/// deseni (BuildPrincipalAsync + ICurrentPrincipalAccessor.Change). [Authorize] KAPATILMAZ (§2:
/// güvenlik gevşetme yasak) — job, gerçek admin yetkileriyle meşru şekilde geçer.</para>
/// <para>Çağıran tenant bağlamını ÖNCE kurmalı (admin o tenant'ta aranır). Admin yoksa null döner —
/// çağıran işi atlar ve loglar (sessiz sahte-başarı yok).</para>
/// </summary>
public class OrchestrationIdentityScope : ITransientDependency
{
    private readonly IdentityUserManager _userManager;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    /// <summary>Tenant kurulumunda seed edilen admin rolü (ABP kuralı; TenantAppService ile aynı sabit).</summary>
    private const string TenantAdminRoleName = "admin";

    public OrchestrationIdentityScope(
        IdentityUserManager userManager,
        ICurrentPrincipalAccessor currentPrincipalAccessor,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _userManager = userManager;
        _currentPrincipalAccessor = currentPrincipalAccessor;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary>Geçerli tenant'ın admin'i olarak kimlik kapsamı açar; admin yoksa null (çağıran ele almalı).</summary>
    public virtual async Task<IDisposable?> EnterTenantAdminAsync()
    {
        var claims = new List<Claim>();

        // Kimlik okuma KENDİ UoW'unda: job bağlamında ambient UoW YOK — repository erişimi UoW'suz patlar.
        // Claim'ler belleğe kopyalanır; principal kapsamı UoW kapandıktan sonra da yaşar.
        using (var uow = _unitOfWorkManager.Begin(requiresNew: false))
        {
            var admins = await _userManager.GetUsersInRoleAsync(TenantAdminRoleName);
            if (admins.Count == 0)
            {
                return null;
            }

            var admin = admins[0];
            claims.Add(new Claim(AbpClaimTypes.UserId, admin.Id.ToString()));
            claims.Add(new Claim(AbpClaimTypes.UserName, admin.UserName));
            if (admin.TenantId.HasValue)
            {
                claims.Add(new Claim(AbpClaimTypes.TenantId, admin.TenantId.Value.ToString()));
            }

            foreach (var role in await _userManager.GetRolesAsync(admin))
            {
                claims.Add(new Claim(AbpClaimTypes.Role, role));
            }

            await uow.CompleteAsync();
        }

        return _currentPrincipalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "Orchestration")));
    }
}
