using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// VoucherAppService guard'ları: company/org scope zorlaması, fiş aitliği, bayat-istemci tespiti,
/// per-tip işlem yetkisi. İş mantığı içermez — yalnız savunma hattı.
/// </summary>
public partial class VoucherAppService
{
    /// <summary>Sızıntı önleme (BalanceSheet ile aynı desen): CompanyId DAİMA working-context'ten
    /// (<see cref="Integration.TradeXpress.MultiCompany.ICurrentCompany"/>) zorlanır — client'tan gelen
    /// CompanyId'ye ASLA güvenilmez. Sahte CompanyId ile başka şirkete fiş/ledger yazılmasını
    /// (ve bilançosuna sızmasını) engeller.</summary>
    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:Voucher:CompanyContextRequired");
        }

        return companyId;
    }

    /// <summary>Şube working şirkete, kasa (varsa) o şubeye ait olmalı — aitlik doğrulaması
    /// (client'ın başka şirketin şube/kasasını göndermesini engeller).</summary>
    private async Task EnsureOrgScopeAsync(Guid companyId, Guid branchId, Guid? vaultId)
    {
        if (!await _branchRepository.AnyAsync(b => b.Id == branchId && b.CompanyId == companyId))
        {
            throw new BusinessException("TradeXpress:Voucher:BranchNotInCompany");
        }

        if (vaultId is { } vid && !await _vaultRepository.AnyAsync(v => v.Id == vid && v.BranchId == branchId))
        {
            throw new BusinessException("TradeXpress:Voucher:VaultNotInBranch");
        }
    }

    /// <summary>Fişi yükler + working şirkete aitliğini doğrular (yabancı şirket fişi = yokmuş gibi davran).</summary>
    private async Task<Voucher> GetOwnedVoucherAsync(Guid voucherId)
    {
        var voucher = await _repository.GetAsync(voucherId);
        if (voucher.CompanyId != EnsureCurrentCompanyId())
        {
            throw new EntityNotFoundException(typeof(Voucher), voucherId);
        }

        return voucher;
    }

    // NOT (eşzamanlılık doğrulaması): PARALEL istek koruması ABP'de ZATEN var — repo UpdateAsync root'u
    // Modified işaretler, ABP stamp'i döndürür (expected-original = property'deki değer) → ikinci paralel
    // istek AbpDbConcurrencyException alır; ledger drift'i bu yolda imkânsız. Stamp'i ELLE set etmek bu
    // mekanizmayı BOZAR (set edilen değer expected-original sanılır → 0 satır → hata) — yapma.
    // Kalan tek gerçek boşluk BAYAT İSTEMCİ idi (form eski veriyle açık) → aşağıdaki kontrol.

    /// <summary>İstemcinin okuduğu andaki fiş stamp'i mevcutla eşleşmiyorsa (arada başka kullanıcı değiştirdi)
    /// kaydı reddeder — sessiz last-writer-wins yerine açık, lokalize uyarı.</summary>
    private static void EnsureVoucherNotStale(Voucher voucher, string? clientStamp)
    {
        if (clientStamp != null && clientStamp != voucher.ConcurrencyStamp)
        {
            throw new BusinessException("TradeXpress:Voucher:ConcurrencyConflict");
        }
    }

    /// <summary>Satırın <see cref="ProcessType"/>'ına göre gerekli yetkiyi kontrol eder — UI gate'i (buton
    /// gizleme) bypass eden doğrudan API çağrılarına karşı SON savunma hattı (ProcessTypePermissionMap tek kaynak).</summary>
    private async Task EnsureTransactionPermissionAsync(ProcessType type)
    {
        var permission = ProcessTypePermissionMap.PermissionFor(type);
        await AuthorizationService.CheckAsync(permission);
    }

    // NOT: Eski private IsInflow yardımcısı Domain.Shared'daki ProcessDirectionTypeExtensions.IsInflow()
    // extension'ına taşındı (DRY — tek kaynak).
}
