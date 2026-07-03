using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
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

    /// <summary>Org grafını kurar ve working şirketi bu şirket yapar.</summary>
    private async Task<VoucherTestData> ArrangeCompanyAsync()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());
        _companyContext.CompanyId = data.CompanyId;
        return data;
    }
}
