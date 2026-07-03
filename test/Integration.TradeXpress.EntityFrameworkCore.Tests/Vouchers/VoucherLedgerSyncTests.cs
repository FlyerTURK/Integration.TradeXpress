using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vouchers.Balance;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fiş → poster → ledger senkron zinciri (E-5/A): <c>SaveLineAsync</c> sonrası
/// <see cref="BalanceLedgerEntry"/>'lerin doğru birim/işaret/tutar + kapsam kopyasıyla yazıldığını,
/// güncellemede idempotent (sil + yeniden yaz) davranışı ve satır silmede ledger düşüşünü doğrular.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class VoucherLedgerSyncTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IVoucherAppService _voucherAppService;
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledgerRepository;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public VoucherLedgerSyncTests()
    {
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _ledgerRepository  = GetRequiredService<IRepository<BalanceLedgerEntry, Guid>>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task Cash_and_metal_lines_write_ledger_with_correct_unit_sign_and_scope()
    {
        var data = await ArrangeCompanyAsync();

        // Nakit GİRİŞ (+1000 TRY) → tek ledger satırı: TRY, +1000 (ALACAK).
        var cash = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 1000m));
        var voucherId = cash.VoucherId!.Value;

        var afterCash = await GetLedgerAsync(voucherId);
        var cashEntry = afterCash.ShouldHaveSingleItem();
        cashEntry.UnitId.ShouldBe(data.TryUnitId);
        cashEntry.Amount.ShouldBe(1000m);
        cashEntry.VoucherLineId.ShouldBe(cash.Id);
        // Kapsam alanları voucher header'ından kopyalanır (rapor scope filtresi).
        cashEntry.CompanyId.ShouldBe(data.CompanyId);
        cashEntry.BranchId.ShouldBe(data.BranchId);
        cashEntry.VaultId.ShouldBe(data.VaultId);
        cashEntry.AccountId.ShouldBe(data.AccountId);
        cashEntry.SubAccountId.ShouldBe(data.SubAccountId);

        // Maden ÇIKIŞ (10 HAS + 150 TRY işçilik) aynı fişe → İKİ bacak, ikisi de eksi (BORÇ).
        var metalDto = VoucherTestLines.MetalLine(data, ProcessDirectionType.Outbound, 10m, 150m);
        metalDto.VoucherId = voucherId;
        var metal = await _voucherAppService.SaveLineAsync(metalDto);

        var afterMetal = await GetLedgerAsync(voucherId);
        afterMetal.Count.ShouldBe(3);   // nakit 1 + maden 2 bacak

        var metalEntries = afterMetal.Where(e => e.VoucherLineId == metal.Id).ToList();
        metalEntries.Count.ShouldBe(2);
        metalEntries.Single(e => e.UnitId == data.HasUnitId).Amount.ShouldBe(-10m);
        metalEntries.Single(e => e.UnitId == data.TryUnitId).Amount.ShouldBe(-150m);

        // Nakit bacağı dokunulmadan durur (senkron fiş-bazlı yeniden yazsa da net etki aynı).
        afterMetal.Single(e => e.VoucherLineId == cash.Id).Amount.ShouldBe(1000m);
    }

    [Fact]
    public async Task Cash_with_cash_payment_does_not_reach_ledger()
    {
        var data = await ArrangeCompanyAsync();

        // Peşin (WithCash) nakit bakiyeye YANSIMAZ → poster boş döner, ledger satırı yazılmaz.
        var line = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 500m, ProcessPaymentType.WithCash));

        (await GetLedgerAsync(line.VoucherId!.Value)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Updating_a_line_rewrites_ledger_idempotently()
    {
        var data = await ArrangeCompanyAsync();

        var saved = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 1000m));
        var voucherId = saved.VoucherId!.Value;

        // Aynı satırı güncelle: yön ÇIKIŞ, tutar 250 → sil+yeniden-yaz sonrası TEK satır, −250.
        var update = VoucherTestLines.CashLine(data, ProcessDirectionType.Outbound, 250m);
        update.Id        = saved.Id;
        update.VoucherId = voucherId;
        await _voucherAppService.SaveLineAsync(update);

        var entries = await GetLedgerAsync(voucherId);
        var entry = entries.ShouldHaveSingleItem();
        entry.UnitId.ShouldBe(data.TryUnitId);
        entry.Amount.ShouldBe(-250m);

        // Idempotens: aynı satırı aynı değerlerle tekrar kaydetmek sonucu DEĞİŞTİRMEZ.
        update.Id        = saved.Id;
        update.VoucherId = voucherId;
        await _voucherAppService.SaveLineAsync(update);

        var again = await GetLedgerAsync(voucherId);
        again.ShouldHaveSingleItem().Amount.ShouldBe(-250m);
    }

    [Fact]
    public async Task Deleting_a_line_removes_its_ledger_entries()
    {
        var data = await ArrangeCompanyAsync();

        var cash = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 1000m));
        var voucherId = cash.VoucherId!.Value;

        var metalDto = VoucherTestLines.MetalLine(data, ProcessDirectionType.Inbound, 5m, 75m);
        metalDto.VoucherId = voucherId;
        var metal = await _voucherAppService.SaveLineAsync(metalDto);

        (await GetLedgerAsync(voucherId)).Count.ShouldBe(3);

        // Nakit satırını sil → yalnız maden bacakları kalır; silinen satırın ledger izi düşer.
        await _voucherAppService.DeleteLineAsync(voucherId, cash.Id, "test silme");

        var remaining = await GetLedgerAsync(voucherId);
        remaining.Count.ShouldBe(2);
        remaining.ShouldAllBe(e => e.VoucherLineId == metal.Id);
    }

    /// <summary>Org grafını kurar ve working şirketi bu şirket yapar.</summary>
    private async Task<VoucherTestData> ArrangeCompanyAsync()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());
        _companyContext.CompanyId = data.CompanyId;
        return data;
    }

    private Task<List<BalanceLedgerEntry>> GetLedgerAsync(Guid voucherId)
    {
        return WithUnitOfWorkAsync(() => _ledgerRepository.GetListAsync(e => e.VoucherId == voucherId));
    }
}
