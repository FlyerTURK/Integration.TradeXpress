using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Yalnız yetki testlerinin startup modülü: test tabanının always-allow'u yerine
/// <see cref="DeniableAuthorizationService"/> geçirilir — böylece per-tip işlem yetkisi denenebilir.
/// Diğer test sınıfları <see cref="TradeXpressEntityFrameworkCoreTestModule"/> ile değişmeden kalır
/// (always-allow bozulmaz); yalnız bu modülü kullanan sınıflar deniable davranışı görür.
/// </summary>
[DependsOn(typeof(TradeXpressEntityFrameworkCoreTestModule))]
public class VoucherPermissionTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Method-invocation (class-level [Authorize]) always-allow kalır; yalnız policy-adlı
        // CheckAsync çağrıları (EnsureTransactionPermissionAsync) deniable servise düşer.
        context.Services.AddSingleton<DeniableAuthorizationService>();
        context.Services.Replace(ServiceDescriptor.Singleton<IAuthorizationService>(
            sp => sp.GetRequiredService<DeniableAuthorizationService>()));
    }
}

/// <summary>
/// Per-tip işlem yetkisi zorlaması (E-2 fix'inin regresyon ağı): Metal izni olmayan kullanıcı
/// Metal satırı KAYDEDEMEZ ve SİLEMEZ (UI gate'i bypass eden doğrudan API çağrısına karşı);
/// izin verilince her iki yol da çalışır.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class VoucherPermissionTests : TradeXpressTestBase<VoucherPermissionTestModule>
{
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly DeniableAuthorizationService _authorization;

    public VoucherPermissionTests()
    {
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
        _authorization     = GetRequiredService<DeniableAuthorizationService>();
    }

    [Fact]
    public async Task Save_metal_line_requires_metal_permission()
    {
        var data = await ArrangeCompanyAsync();

        // Metal izni YOK → kayıt reddedilir (fiş de oluşmaz).
        _authorization.DeniedPolicies.Add(TradeXpressPermissions.Transactions.Metal);
        await Should.ThrowAsync<AbpAuthorizationException>(
            () => _voucherAppService.SaveLineAsync(
                VoucherTestLines.MetalLine(data, ProcessDirectionType.Inbound, 10m, 150m)));

        // Metal izni reddi NAKİT'i etkilemez (yetki per-tip'tir).
        var cash = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 100m));
        cash.VoucherId.ShouldNotBeNull();

        // İzin verilince aynı Metal satırı geçer.
        _authorization.DeniedPolicies.Clear();
        var metal = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.MetalLine(data, ProcessDirectionType.Inbound, 10m, 150m));
        metal.VoucherId.ShouldNotBeNull();
        metal.Id.ShouldNotBe(default);
    }

    [Fact]
    public async Task Delete_line_requires_same_process_type_permission()
    {
        var data = await ArrangeCompanyAsync();

        // İzinliyken Metal satırı oluştur.
        var metal = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.MetalLine(data, ProcessDirectionType.Inbound, 10m, 150m));
        var voucherId = metal.VoucherId!.Value;

        // Silme de Save ile AYNI per-tip yetkiye tabi: Metal izni yokken silinemez.
        _authorization.DeniedPolicies.Add(TradeXpressPermissions.Transactions.Metal);
        await Should.ThrowAsync<AbpAuthorizationException>(
            () => _voucherAppService.DeleteLineAsync(voucherId, metal.Id, "yetkisiz silme denemesi"));

        // Satır hâlâ yerinde.
        (await _voucherAppService.GetLinesAsync(voucherId)).ShouldHaveSingleItem();

        // İzin verilince silme geçer.
        _authorization.DeniedPolicies.Clear();
        await _voucherAppService.DeleteLineAsync(voucherId, metal.Id, "izinli silme");
        (await _voucherAppService.GetLinesAsync(voucherId)).ShouldBeEmpty();
    }

    // ---- Faz 4 working-context YETKİSİ: şube/kasa scope grant zorlaması (EnsureOrgScopeAsync) ----

    /// <summary>Kullanıcı yalnız BranchA'ya grant'lıyken aynı şirketteki BranchB'ye fiş yazamaz
    /// (yapısal aitlik geçse bile YETKİ katmanı reddeder) — tenant-geneli grant YOK (daraltılmış kullanıcı).</summary>
    [Fact]
    public async Task Posting_to_unauthorized_branch_is_rejected()
    {
        // Tenant-geneli grant KURMADAN (daraltılmış kullanıcı) org grafı + ikinci şube + yalnız BranchA grant'ı.
        var (data, otherBranchId, otherVaultId) = await WithUnitOfWorkAsync(async () =>
        {
            var d = await _seeder.SeedCompanyGraphAsync(grantTenantWideAccess: false);
            var (branchB, vaultB) = await _seeder.SeedExtraBranchAsync(d);
            await _seeder.GrantBranchAsync(d.CompanyId, d.BranchId);   // yalnız BranchA yetkili
            return (d, branchB, vaultB);
        });
        _companyContext.CompanyId = data.CompanyId;

        // BranchA'ya yazım GEÇER (grant'lı).
        var allowed = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 100m));
        allowed.VoucherId.ShouldNotBeNull();

        // BranchB'ye yazım (yapısal olarak şirkete ait, ama kullanıcı yetkisiz) → BranchNotAuthorized.
        var toBranchB = VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 100m);
        toBranchB.BranchId = otherBranchId;
        toBranchB.VaultId  = otherVaultId;

        (await Should.ThrowAsync<BusinessException>(() => _voucherAppService.SaveLineAsync(toBranchB)))
            .Code.ShouldBe("TradeXpress:Voucher:BranchNotAuthorized");
    }

    /// <summary>Tenant-geneli grant (mevcut davranış) altında yetki katmanı NO-OP'tur: kullanıcı şirketteki
    /// HER şubeye (varsayılan + ek) yazabilir.</summary>
    [Fact]
    public async Task Tenant_wide_grant_allows_posting_to_any_branch()
    {
        // ArrangeCompanyAsync tenant-geneli grant seed'ler (varsayılan) + ek bir şube.
        var (data, otherBranchId, otherVaultId) = await WithUnitOfWorkAsync(async () =>
        {
            var d = await _seeder.SeedCompanyGraphAsync();   // tenant-geneli grant (no-op eşdeğeri)
            var (branchB, vaultB) = await _seeder.SeedExtraBranchAsync(d);
            return (d, branchB, vaultB);
        });
        _companyContext.CompanyId = data.CompanyId;

        // Varsayılan şube.
        (await _voucherAppService.SaveLineAsync(
            VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 100m))).VoucherId.ShouldNotBeNull();

        // Ek şube de yazılabilir (tenant-geneli grant tüm şubeleri kapsar).
        var toBranchB = VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 100m);
        toBranchB.BranchId = otherBranchId;
        toBranchB.VaultId  = otherVaultId;
        (await _voucherAppService.SaveLineAsync(toBranchB)).VoucherId.ShouldNotBeNull();
    }

    /// <summary>Kasa seviyesi: şube grant'lı ama tek bir kasa Deny'liyken o kasaya yazım reddedilir
    /// (VaultNotAuthorized); aynı şubeye kasasız (VaultId=null) yazım GEÇER (yalnız şube kararına bakılır).</summary>
    [Fact]
    public async Task Posting_to_denied_vault_is_rejected_but_branch_level_write_passes()
    {
        var data = await WithUnitOfWorkAsync(async () =>
        {
            var d = await _seeder.SeedCompanyGraphAsync(grantTenantWideAccess: false);
            await _seeder.GrantBranchAsync(d.CompanyId, d.BranchId);        // şube yetkili
            await _seeder.DenyVaultAsync(d.CompanyId, d.BranchId, d.VaultId); // ama bu kasa kapalı
            return d;
        });
        _companyContext.CompanyId = data.CompanyId;

        // Deny'li kasaya yazım → VaultNotAuthorized (şube geçer, kasa reddeder).
        var toVault = VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 100m);
        (await Should.ThrowAsync<BusinessException>(() => _voucherAppService.SaveLineAsync(toVault)))
            .Code.ShouldBe("TradeXpress:Voucher:VaultNotAuthorized");

        // Aynı şubeye kasasız yazım → GEÇER (kasa kararı devreye girmez).
        var branchOnly = VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 100m);
        branchOnly.VaultId = null;
        (await _voucherAppService.SaveLineAsync(branchOnly)).VoucherId.ShouldNotBeNull();
    }

    /// <summary>Org grafını kurar ve working şirketi bu şirket yapar (tenant-geneli grant seed'lenir — no-op).</summary>
    private async Task<VoucherTestData> ArrangeCompanyAsync()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());
        _companyContext.CompanyId = data.CompanyId;
        return data;
    }
}
