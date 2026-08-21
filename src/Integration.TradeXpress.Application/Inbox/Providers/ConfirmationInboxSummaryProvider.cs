using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.TradeXpress.Authorization;
using Integration.TradeXpress.Confirmations;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Vaults;
using Microsoft.Extensions.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Users;

namespace Integration.TradeXpress.Inbox.Providers;

/// <summary>
/// Ortak gelen kutusunun TEYİT kartı — <see cref="Confirmation"/> aggregate'inden SALT OKUMA özet üretir.
///
/// <para><b>Teyit modülüne DOKUNULMAZ (2026-08-01 Hakan kararı):</b> mevcut ekran/entity/servis TAŞINMAZ ve
/// DEĞİŞTİRİLMEZ; pano ondan yalnız sayı + son birkaç satır okur. Bu yüzden burada
/// <see cref="IConfirmationAppService"/> DEĞİL doğrudan repository kullanılır — app service çağırmak Teyit'in
/// yetki/DTO sözleşmesini panoya bağlar ve o modülün sözleşmesini panonun ihtiyacına göre büyütme baskısı yaratırdı.
/// Karşılığında GÖRÜNÜRLÜK KURALINI burada TEKRAR uygulamak zorundayız (aşağı bak) — sapmaması gereken tek yer.</para>
///
/// <para><b>Görünürlük = <c>ConfirmationAppService.GetListAsync</c> ile AYNI kural:</b> working şirketin teyitleri
/// içinde, kullanıcının BAŞLATAN <b>veya</b> KARŞI kasasına erişebildikleri (giden kutusu + gelen kutusu). Oradaki
/// uygulama satır satır <c>CanAccessVault</c> çağırır; burada eşdeğeri ÖNCE erişilebilir kasa kümesi çözülerek
/// kurulur ve daraltma SQL'e iner. Sonuç aynıdır (görünürlük yalnız kasa erişimine bağlıdır) ama pano her
/// açılışta şirketin TÜM teyitlerini belleğe çekmez — kart bir sayaçtır, liste ekranı değil.</para>
///
/// <para><b>Bekleyen</b> = <see cref="ConfirmationStatus.Proposed"/> | <see cref="ConfirmationStatus.Declared"/>:
/// taraflardan biri hâlâ aksiyon borçludur. Confirmed/Rejected kapanmış durumlardır — sayaçta yer almaz ama
/// <c>TotalCount</c>'ta ve son satırlarda görünür (iz sürülsün).</para>
/// </summary>
[ExposeServices(typeof(IInboxSummaryProvider))]
public class ConfirmationInboxSummaryProvider : IInboxSummaryProvider, ITransientDependency
{
    /// <summary>Teyit tam ekranının GERÇEK rotası — <c>ConfirmationInboxPage.razor</c> <c>@page</c>'inden okundu.</summary>
    private const string ConfirmationsRoute = "/confirmations";

    /// <summary><c>TradeXpressIcons.Confirmation</c> sabitinin değeri (Teyitler menüsünde kullanılan ikonun aynısı).
    /// <para>Sabit <c>Blazor.Client</c>'ta yaşar ve Application katmanı UI'ya referans VEREMEZ (katman yönü
    /// UI→Application) → değer burada birebir tekrarlanır. Değişirse iki yer birlikte güncellenir.</para></summary>
    private const string ConfirmationIconCssClass = "custom-icon-check-circle";

    /// <summary>Kasa kodu çözülemezse gösterilecek yer tutucu (şirket-içi teyitte normalde oluşmaz).</summary>
    private const string UnknownVaultCode = "?";

    /// <summary>"Dikkat bekleyen" ölçütü — TEK KAYNAK. Aynı ifade hem SQL sayımında (<see cref="PendingExpression"/>)
    /// hem son satırların bayrağında (<see cref="IsPending"/>) kullanılır; iki kopya sapamaz.</summary>
    private static readonly Expression<Func<Confirmation, bool>> PendingExpression =
        confirmation => confirmation.Status == ConfirmationStatus.Proposed
                        || confirmation.Status == ConfirmationStatus.Declared;

    private static readonly Func<Confirmation, bool> IsPending = PendingExpression.Compile();

    private readonly IRepository<Confirmation, Guid> _confirmationRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly IScopedGrantResolver _scopedGrantResolver;
    private readonly IPermissionChecker _permissionChecker;
    private readonly ICurrentUser _currentUser;
    private readonly IStringLocalizer<TradeXpressResource> _localizer;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public ConfirmationInboxSummaryProvider(
        IRepository<Confirmation, Guid> confirmationRepository,
        IRepository<Vault, Guid> vaultRepository,
        ICurrentCompany currentCompany,
        IScopedGrantResolver scopedGrantResolver,
        IPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IStringLocalizer<TradeXpressResource> localizer,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _confirmationRepository = confirmationRepository;
        _vaultRepository        = vaultRepository;
        _currentCompany         = currentCompany;
        _scopedGrantResolver    = scopedGrantResolver;
        _permissionChecker      = permissionChecker;
        _currentUser            = currentUser;
        _localizer              = localizer;
        _asyncExecuter          = asyncExecuter;
    }

    public string SourceKey
    {
        get
        {
            return InboxSourceKey.Confirmations;
        }
    }

    public int Order
    {
        get
        {
            return 1;
        }
    }

    /// <summary>Teyit kartını kurar. <b>null</b> = kart gösterilmez: kimlik/izin/şirket bağlamı ya da
    /// erişilebilir kasa yoksa kullanıcının bu türde görebileceği HİÇBİR kayıt yoktur — daima "0" okuyan
    /// bir kart panoyu kirletir.</summary>
    public async Task<InboxCardDto?> BuildCardAsync(int recentCount)
    {
        if (_currentUser.Id is not { } userId)
        {
            return null;
        }

        if (!await _permissionChecker.IsGrantedAsync(TradeXpressPermissions.Confirmations.View))
        {
            return null;
        }

        // Şirket bağlamı ZORUNLU: Teyit company-scoped'tır ve şirket filtresi null bağlamda permissive'dir
        // (ChannelQuestionAppService ile aynı fail-closed gerekçesi) — kapsamsız okuma kazara veri sızdırmasın.
        if (_currentCompany.Id is not { } companyId)
        {
            return null;
        }

        var vaults = await GetCompanyVaultsAsync(companyId);
        var accessibleVaultIds = await ResolveAccessibleVaultIdsAsync(userId, companyId, vaults);
        if (accessibleVaultIds.Count == 0)
        {
            return null;
        }

        // İki-taraflı görünürlük (giden + gelen): taraflardan BİRİNE erişim yeterlidir.
        var query = (await _confirmationRepository.GetQueryableAsync())
            .Where(confirmation => confirmation.CompanyId == companyId
                                   && (accessibleVaultIds.Contains(confirmation.InitiatorVaultId)
                                       || accessibleVaultIds.Contains(confirmation.CounterpartyVaultId)));

        var totalCount   = await _asyncExecuter.CountAsync(query);
        var pendingCount = await _asyncExecuter.CountAsync(query.Where(PendingExpression));

        // Kod eşlemesi ŞİRKETİN TÜM kasalarından kurulur (yalnız erişilebilirlerden değil): karşı taraf
        // kullanıcının erişemediği bir kasa olabilir ama satır yine de görünür — kodu boş kalmamalı.
        var vaultCodes  = vaults.ToDictionary(vault => vault.Id, vault => vault.Code);
        var recentItems = await BuildRecentItemsAsync(query, recentCount, vaultCodes);

        return new InboxCardDto
        {
            SourceKey    = InboxSourceKey.Confirmations,
            Title        = _localizer["Menu:Confirmations"],
            IconCssClass = ConfirmationIconCssClass,
            PendingCount = pendingCount,
            TotalCount   = totalCount,
            TargetUrl    = ConfirmationsRoute,
            RecentItems  = recentItems,
        };
    }

    /// <summary>Şirketin kasaları — hem erişim çözümü hem kod gösterimi için TEK sorguda çekilir.
    /// Kasa sayısı şirket başına sınırlıdır (şube × kasa), teyit sayısı değil.</summary>
    private async Task<List<Vault>> GetCompanyVaultsAsync(Guid companyId)
    {
        return await _asyncExecuter.ToListAsync(
            (await _vaultRepository.GetQueryableAsync()).Where(vault => vault.CompanyId == companyId));
    }

    /// <summary>Kullanıcının working şirkette erişebildiği kasa id'leri (working-context scope;
    /// en-spesifik-kazanır, DENY aynı seviyede üstün — karar <see cref="ScopedAccessSet"/>'te).</summary>
    private async Task<List<Guid>> ResolveAccessibleVaultIdsAsync(Guid userId, Guid companyId, List<Vault> vaults)
    {
        if (vaults.Count == 0)
        {
            return new List<Guid>();
        }

        var access = await _scopedGrantResolver.ResolveAsync(userId);

        return vaults
            .Where(vault => access.CanAccessVault(companyId, vault.BranchId, vault.Id))
            .Select(vault => vault.Id)
            .ToList();
    }

    /// <summary>Kartın önizleme satırları: EN YENİ teyitler (durum ayırt etmeden — kapanmış olan da izlenebilsin).
    /// Sıralama <c>CreationTime</c> azalan; eşitlikte Id ile kararlı kırılır.</summary>
    private async Task<List<InboxCardItemDto>> BuildRecentItemsAsync(
        IQueryable<Confirmation> query,
        int recentCount,
        IReadOnlyDictionary<Guid, string> vaultCodes)
    {
        if (recentCount <= 0)
        {
            return new List<InboxCardItemDto>();
        }

        var recent = await _asyncExecuter.ToListAsync(
            query
                .OrderByDescending(confirmation => confirmation.CreationTime)
                .ThenByDescending(confirmation => confirmation.Id)
                .Take(recentCount));

        return recent
            .Select(confirmation => new InboxCardItemDto
            {
                Id            = confirmation.Id,
                PrimaryText   = BuildVaultPairText(confirmation, vaultCodes),
                SecondaryText = _localizer[$"Enum:ProcessType:{confirmation.ProcessType}"],
                // Timestamp UTC saklanır; yerel saate çeviri UI'nın işi (kayıt=UTC / görüntü=yerel kuralı).
                OccurredAt    = confirmation.CreationTime,
                IsPending     = IsPending(confirmation),
            })
            .ToList();
    }

    /// <summary>Satırın kimliği: hangi kasadan hangi kasaya. Tek bir "karşı taraf" yazmak yerine ÇİFT
    /// yazılır — kullanıcı iki tarafa da erişiyor olabilir, o durumda "karşı taraf" belirsizleşirdi.</summary>
    private static string BuildVaultPairText(
        Confirmation confirmation,
        IReadOnlyDictionary<Guid, string> vaultCodes)
    {
        var initiator    = vaultCodes.GetValueOrDefault(confirmation.InitiatorVaultId) ?? UnknownVaultCode;
        var counterparty = vaultCodes.GetValueOrDefault(confirmation.CounterpartyVaultId) ?? UnknownVaultCode;

        return $"{initiator} → {counterparty}";
    }
}
