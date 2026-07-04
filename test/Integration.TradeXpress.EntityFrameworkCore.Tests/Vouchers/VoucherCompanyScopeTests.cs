using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Company-scope güvenlik zorlaması regresyon ağı (E-1 fix'i): CompanyId client'tan ALINMAZ —
/// ambient <see cref="ICurrentCompany"/> zorlanır; şube→şirket / kasa→şube aitliği doğrulanır;
/// yabancı şirketin fişi "yokmuş gibi" davranılır; okuma yolları working şirkete filtrelenir.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class VoucherCompanyScopeTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public VoucherCompanyScopeTests()
    {
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task Save_line_without_company_context_is_rejected()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());
        _companyContext.CompanyId = null;   // working şirket YOK (API/anonim bağlam eşdeğeri)

        var ex = await Should.ThrowAsync<BusinessException>(
            () => _voucherAppService.SaveLineAsync(
                VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 100m)));

        ex.Code.ShouldBe("TradeXpress:Voucher:CompanyContextRequired");
    }

    [Fact]
    public async Task Read_paths_without_company_context_are_rejected()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());
        _companyContext.CompanyId = null;

        // Okuma yolları da context'siz çalışmaz — sızıntı önlemenin okuma ayağı.
        (await Should.ThrowAsync<BusinessException>(
                () => _voucherAppService.GetListAsync(new VoucherListRequestDto { SubAccountId = data.SubAccountId })))
            .Code.ShouldBe("TradeXpress:Voucher:CompanyContextRequired");

        (await Should.ThrowAsync<BusinessException>(
                () => _voucherAppService.GetBalancesAsync(data.SubAccountId)))
            .Code.ShouldBe("TradeXpress:Voucher:CompanyContextRequired");
    }

    [Fact]
    public async Task Foreign_branch_or_vault_is_rejected()
    {
        var (mine, foreign) = await ArrangeTwoCompaniesAsync();
        _companyContext.CompanyId = mine.CompanyId;

        // Başka şirketin şubesiyle satır → şube aitlik doğrulaması reddeder.
        var foreignBranch = VoucherTestLines.CashLine(mine, ProcessDirectionType.Inbound, 100m);
        foreignBranch.BranchId = foreign.BranchId;
        foreignBranch.VaultId  = null;

        (await Should.ThrowAsync<BusinessException>(() => _voucherAppService.SaveLineAsync(foreignBranch)))
            .Code.ShouldBe("TradeXpress:Voucher:BranchNotInCompany");

        // Şube doğru ama kasa başka şubenin → kasa aitlik doğrulaması reddeder.
        var foreignVault = VoucherTestLines.CashLine(mine, ProcessDirectionType.Inbound, 100m);
        foreignVault.VaultId = foreign.VaultId;

        (await Should.ThrowAsync<BusinessException>(() => _voucherAppService.SaveLineAsync(foreignVault)))
            .Code.ShouldBe("TradeXpress:Voucher:VaultNotInBranch");
    }

    [Fact]
    public async Task Foreign_company_voucher_behaves_as_not_found()
    {
        var (mine, foreign) = await ArrangeTwoCompaniesAsync();

        // Yabancı şirkette fiş oluştur.
        _companyContext.CompanyId = foreign.CompanyId;
        var foreignLine = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.CashLine(foreign, ProcessDirectionType.Inbound, 1000m));
        var foreignVoucherId = foreignLine.VoucherId!.Value;

        // Working şirket bize dönünce yabancı fiş YOKMUŞ gibi davranılır (id sızsa bile).
        _companyContext.CompanyId = mine.CompanyId;

        await Should.ThrowAsync<EntityNotFoundException>(
            () => _voucherAppService.GetLinesAsync(foreignVoucherId));

        await Should.ThrowAsync<EntityNotFoundException>(
            () => _voucherAppService.DeleteLineAsync(foreignVoucherId, foreignLine.Id, "sızıntı denemesi"));

        // Yabancı fişe satır ekleme/güncelleme de aynı şekilde reddedilir.
        var attach = VoucherTestLines.CashLine(mine, ProcessDirectionType.Inbound, 50m);
        attach.VoucherId = foreignVoucherId;
        await Should.ThrowAsync<EntityNotFoundException>(() => _voucherAppService.SaveLineAsync(attach));
    }

    [Fact]
    public async Task Balances_aggregate_only_working_company_lines()
    {
        var (mine, foreign) = await ArrangeTwoCompaniesAsync();

        // Yabancı şirketin carisine +1000 TRY yaz.
        _companyContext.CompanyId = foreign.CompanyId;
        await _voucherAppService.SaveLineAsync(
            VoucherTestLines.CashLine(foreign, ProcessDirectionType.Inbound, 1000m));

        // Working = bizim şirket: yabancı cari id'siyle bakiye sorgusu HİÇBİR hareket görmez.
        _companyContext.CompanyId = mine.CompanyId;
        var leaked = await _voucherAppService.GetBalancesAsync(foreign.SubAccountId);
        leaked.Lines.Where(l => l.Net != 0m).ShouldBeEmpty();

        // Working = yabancı şirket: kendi hareketi görünür (kontrol grubu).
        _companyContext.CompanyId = foreign.CompanyId;
        var owned = await _voucherAppService.GetBalancesAsync(foreign.SubAccountId);
        var nonZero = owned.Lines.Where(l => l.Net != 0m).ShouldHaveSingleItem();
        nonZero.UnitId.ShouldBe(foreign.TryUnitId);
        nonZero.Net.ShouldBe(1000m);
    }

    [Fact]
    public async Task Foreign_company_line_edit_behaves_as_not_found()
    {
        var (mine, foreign) = await ArrangeTwoCompaniesAsync();

        // Yabancı şirkette satır oluştur.
        _companyContext.CompanyId = foreign.CompanyId;
        var foreignLine = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.CashLine(foreign, ProcessDirectionType.Inbound, 1000m));

        // Working şirket bize dönünce yabancı satır düzenlemeye AÇILAMAZ (id sızsa bile yokmuş gibi).
        _companyContext.CompanyId = mine.CompanyId;
        await Should.ThrowAsync<EntityNotFoundException>(
            () => _voucherAppService.GetLineForEditAsync(foreignLine.Id));

        // Kontrol grubu: sahibi şirket bağlamında aynı satır normal açılır.
        _companyContext.CompanyId = foreign.CompanyId;
        (await _voucherAppService.GetLineForEditAsync(foreignLine.Id)).Id.ShouldBe(foreignLine.Id);
    }

    [Fact]
    public async Task Foreign_company_bullion_entry_is_not_found_for_exit()
    {
        var (mine, foreign) = await ArrangeTwoCompaniesAsync();

        // Yabancı şirkette takoz GİRİŞ külçesi oluştur.
        _companyContext.CompanyId = foreign.CompanyId;
        var entry = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.BullionEntryLine(foreign, "TKZ-FOREIGN", 100m));

        // Working = bizim şirket: yabancı külçe id'siyle takoz ÇIKIŞ hazırlanamaz (yokmuş gibi).
        _companyContext.CompanyId = mine.CompanyId;
        var exit = VoucherTestLines.BullionExitLine(mine, entry.Id);

        (await Should.ThrowAsync<BusinessException>(() => _voucherAppService.SaveLineAsync(exit)))
            .Code.ShouldBe("TradeXpress:Bullion:ExitEntryNotFound");
    }

    /// <summary>Aynı tenant/host altında iki ayrı şirket grafı kurar (sızıntı senaryoları).</summary>
    private async Task<(VoucherTestData Mine, VoucherTestData Foreign)> ArrangeTwoCompaniesAsync()
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var mine    = await _seeder.SeedCompanyGraphAsync("TS1");
            var foreign = await _seeder.SeedCompanyGraphAsync("TS2");
            return (mine, foreign);
        });
    }
}
