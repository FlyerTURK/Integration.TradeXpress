using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vouchers.Balance;
using Shouldly;
using Volo.Abp;
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
    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public VoucherLedgerSyncTests()
    {
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _ledgerRepository  = GetRequiredService<IRepository<BalanceLedgerEntry, Guid>>();
        _voucherRepository = GetRequiredService<IRepository<Voucher, Guid>>();
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

        // Maden ÇIKIŞ (10 HAS + 150 TRY işçilik) aynı fişe → İKİ leg, ikisi de eksi (BORÇ).
        var metalDto = VoucherTestLines.MetalLine(data, ProcessDirectionType.Outbound, 10m, 150m);
        metalDto.VoucherId = voucherId;
        var metal = await _voucherAppService.SaveLineAsync(metalDto);

        var afterMetal = await GetLedgerAsync(voucherId);
        afterMetal.Count.ShouldBe(3);   // nakit 1 + maden 2 leg

        var metalEntries = afterMetal.Where(e => e.VoucherLineId == metal.Id).ToList();
        metalEntries.Count.ShouldBe(2);
        metalEntries.Single(e => e.UnitId == data.HasUnitId).Amount.ShouldBe(-10m);
        metalEntries.Single(e => e.UnitId == data.TryUnitId).Amount.ShouldBe(-150m);

        // Nakit leg'i dokunulmadan durur (senkron fiş-bazlı yeniden yazsa da net etki aynı).
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
    public async Task Metal_reservation_line_does_not_reach_ledger()
    {
        var data = await ArrangeCompanyAsync();

        // Rezervasyon (taahhüt sayacı) bakiyeye YANSIMAZ → ana Has + işçilik dolu olsa bile
        // poster boş döner, hiçbir ledger leg'i yazılmaz.
        var dto = VoucherTestLines.MetalLine(data, ProcessDirectionType.Outbound, 10m, 150m);
        dto.PaymentType = ProcessPaymentType.Reservation;
        var line = await _voucherAppService.SaveLineAsync(dto);

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

        // Nakit satırını sil → yalnız maden leg'leri kalır; silinen satırın ledger izi düşer.
        await _voucherAppService.DeleteLineAsync(voucherId, cash.Id, "test silme");

        var remaining = await GetLedgerAsync(voucherId);
        remaining.Count.ShouldBe(2);
        remaining.ShouldAllBe(e => e.VoucherLineId == metal.Id);
    }

    [Fact]
    public async Task DebitNote_lines_post_signed_pay_leg_in_both_directions()
    {
        var data = await ArrangeCompanyAsync();

        // Dekont GİRİŞ (ALACAK etiketli) → +500 TRY.
        var credit = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.DebitNoteLine(data, ProcessDirectionType.Inbound, 500m));
        var voucherId = credit.VoucherId!.Value;

        // Dekont ÇIKIŞ (BORÇ etiketli) aynı fişe → −200 TRY.
        var debitDto = VoucherTestLines.DebitNoteLine(data, ProcessDirectionType.Outbound, 200m);
        debitDto.VoucherId = voucherId;
        var debit = await _voucherAppService.SaveLineAsync(debitDto);

        var entries = await GetLedgerAsync(voucherId);
        entries.Count.ShouldBe(2);
        entries.Single(e => e.VoucherLineId == credit.Id).Amount.ShouldBe(500m);
        entries.Single(e => e.VoucherLineId == debit.Id).Amount.ShouldBe(-200m);
        entries.ShouldAllBe(e => e.UnitId == data.TryUnitId);
    }

    [Fact]
    public async Task Assay_exit_posts_negative_has_and_gum_legs_without_money_leg()
    {
        var data = await ArrangeCompanyAsync();

        // Çeşni ÇIKIŞ: 10 gr × AU 0.916 / AG 0.05 → HAS −9.16, GUM −0.50; para leg'i YOK.
        var assay = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.AssayLine(data, 10m, 0.916m, 0.05m));

        var entries = await GetLedgerAsync(assay.VoucherId!.Value);
        entries.Count.ShouldBe(2);
        entries.Single(e => e.UnitId == data.HasUnitId).Amount.ShouldBe(-9.16m);
        entries.Single(e => e.UnitId == data.GumUnitId).Amount.ShouldBe(-0.50m);
    }

    [Fact]
    public async Task Assay_stock_accumulates_from_bullion_entries_and_drains_with_assay_exits()
    {
        var data = await ArrangeCompanyAsync();

        // Takoz GİRİŞ: Miktar=100, numune (AssayAmount)=5, AU=0.916, AG=0.04 → çeşni havuzu 5 gr.
        var bullionIn = new VoucherLineDto
        {
            BranchId      = data.BranchId,
            VaultId       = data.VaultId,
            AccountId     = data.AccountId,
            SubAccountId  = data.SubAccountId,
            Type          = ProcessType.Bullion,
            Direction     = ProcessDirectionType.Inbound,
            CommodityCode = "TKZ-1",
            Amount        = 100m,
            Factor        = 0.916m,
            SilverFactor  = 0.04m,
            AssayAmount   = 5m,
            MainUnitId    = data.HasUnitId,
        };
        await _voucherAppService.SaveLineAsync(bullionIn);

        var afterEntry = await WithUnitOfWorkAsync(() => _voucherAppService.GetAssayStockAsync());
        afterEntry.Amount.ShouldBe(5m);
        afterEntry.AuMilyem.ShouldBe(0.916m);
        afterEntry.AgMilyem.ShouldBe(0.04m);

        // Çeşni ÇIKIŞ 2 gr → havuz 3 gr'a düşer; milyem ortalaması değişmez (aynı milyemle çıkış).
        await _voucherAppService.SaveLineAsync(VoucherTestLines.AssayLine(data, 2m, 0.916m, 0.04m));

        var afterExit = await WithUnitOfWorkAsync(() => _voucherAppService.GetAssayStockAsync());
        afterExit.Amount.ShouldBe(3m);
        afterExit.Has.ShouldBe(3m * 0.916m);
        afterExit.Gum.ShouldBe(3m * 0.04m);
        afterExit.AuMilyem.ShouldBe(0.916m);
        afterExit.AgMilyem.ShouldBe(0.04m);
    }

    [Fact]
    public async Task Sub_cent_raw_effect_is_persisted_rounded_to_two_decimals_away_from_zero()
    {
        var data = await ArrangeCompanyAsync();

        // Çeşni ÇIKIŞ: 10 gr × AU 0.9165 → HAM etki −9.1650 (kuruş-altı, tam midpoint);
        // AG 0.0415 × 10 → HAM −0.4150. Poster ara hesabı HAM kalır, ledger KAYIT ANINDA
        // N2 + AwayFromZero yuvarlar (FinancialRounding — ERPPRO SQL scale paritesi):
        // −9.165 → −9.17 (ToEven olsaydı −9.16 yazılırdı — ayrıştırıcı senaryo), −0.415 → −0.42.
        var assay = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.AssayLine(data, 10m, 0.9165m, 0.0415m));

        var entries = await GetLedgerAsync(assay.VoucherId!.Value);
        entries.Count.ShouldBe(2);
        entries.Single(e => e.UnitId == data.HasUnitId).Amount.ShouldBe(-9.17m);
        entries.Single(e => e.UnitId == data.GumUnitId).Amount.ShouldBe(-0.42m);
    }

    [Fact]
    public async Task Assay_line_without_amount_is_rejected()
    {
        var data = await ArrangeCompanyAsync();

        // Miktar ZORUNLU (legacy: CESNI muaf DEĞİL) → 0 miktar lokalize BusinessException'la reddedilir.
        var ex = await Should.ThrowAsync<BusinessException>(
            () => _voucherAppService.SaveLineAsync(VoucherTestLines.AssayLine(data, 0m, 0.916m, 0.05m)));
        ex.Code.ShouldBe("TradeXpress:Voucher:AmountRequired");
    }

    [Fact]
    public async Task Transfer_creates_opposite_twin_in_counter_voucher_with_matching_link()
    {
        var data      = await ArrangeCompanyAsync();
        var counterId = await WithUnitOfWorkAsync(() => _seeder.SeedCounterSubAccountAsync(data));

        // Kaynak taraf ÇIKIŞ (BORÇ) 500 TRY → kaynak fişte −500; ikiz karşı fişte GİRİŞ +500.
        var saved = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.TransferLine(data, counterId, ProcessDirectionType.Outbound, 500m));

        saved.LinkId.ShouldNotBeNull();

        // Kaynak fiş ledger'ı: tek satır, −500 TRY, kaynak alt hesap kapsamı.
        var sourceEntries = await GetLedgerAsync(saved.VoucherId!.Value);
        var sourceEntry = sourceEntries.ShouldHaveSingleItem();
        sourceEntry.UnitId.ShouldBe(data.TryUnitId);
        sourceEntry.Amount.ShouldBe(-500m);
        sourceEntry.SubAccountId.ShouldBe(data.SubAccountId);

        // İkiz satır: aynı LinkId, zıt yön, aynı tutar/birim, karşı referans kaynağa döner.
        var twin = await GetTwinLineAsync(saved.LinkId!.Value, saved.Id);
        twin.ShouldNotBeNull();
        twin.Direction.ShouldBe(ProcessDirectionType.Inbound);
        twin.PayTotal.ShouldBe(500m);
        twin.PayUnitId.ShouldBe(data.TryUnitId);
        twin.CounterAccountId.ShouldBe(data.SubAccountId);
        twin.VoucherId.ShouldNotBe(saved.VoucherId!.Value);   // karşı leg KENDİ fişinde (fiş = tek cari)

        // Karşı fiş ledger'ı: tek satır, +500 TRY, karşı alt hesap kapsamı.
        var twinEntries = await GetLedgerAsync(twin.VoucherId);
        var twinEntry = twinEntries.ShouldHaveSingleItem();
        twinEntry.UnitId.ShouldBe(data.TryUnitId);
        twinEntry.Amount.ShouldBe(500m);
        twinEntry.SubAccountId.ShouldBe(counterId);
    }

    [Fact]
    public async Task Updating_a_transfer_line_updates_its_twin_and_both_ledgers()
    {
        var data      = await ArrangeCompanyAsync();
        var counterId = await WithUnitOfWorkAsync(() => _seeder.SeedCounterSubAccountAsync(data));

        var saved = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.TransferLine(data, counterId, ProcessDirectionType.Outbound, 500m));

        // Güncelle: tutar 300 + yön GİRİŞ'e çevrilir → ikiz de 300 + ÇIKIŞ olmalı (LinkId sabit).
        var update = VoucherTestLines.TransferLine(data, counterId, ProcessDirectionType.Inbound, 300m);
        update.Id        = saved.Id;
        update.VoucherId = saved.VoucherId;
        var updated = await _voucherAppService.SaveLineAsync(update);

        updated.LinkId.ShouldBe(saved.LinkId);   // LinkId güncellemede korunur (sunucu otoritedir)

        (await GetLedgerAsync(saved.VoucherId!.Value)).ShouldHaveSingleItem().Amount.ShouldBe(300m);

        var twin = await GetTwinLineAsync(saved.LinkId!.Value, saved.Id);
        twin.ShouldNotBeNull();
        twin.Direction.ShouldBe(ProcessDirectionType.Outbound);
        twin.PayTotal.ShouldBe(300m);
        (await GetLedgerAsync(twin.VoucherId)).ShouldHaveSingleItem().Amount.ShouldBe(-300m);
    }

    [Fact]
    public async Task Deleting_a_transfer_line_removes_twin_line_and_both_ledgers()
    {
        var data      = await ArrangeCompanyAsync();
        var counterId = await WithUnitOfWorkAsync(() => _seeder.SeedCounterSubAccountAsync(data));

        var saved = await _voucherAppService.SaveLineAsync(
            VoucherTestLines.TransferLine(data, counterId, ProcessDirectionType.Outbound, 750m));

        var twinBefore = await GetTwinLineAsync(saved.LinkId!.Value, saved.Id);
        twinBefore.ShouldNotBeNull();

        // Kaynak satırı sil → ikiz de düşer; İKİ fişin ledger'ı da temizlenir (tek leg kalamaz).
        await _voucherAppService.DeleteLineAsync(saved.VoucherId!.Value, saved.Id, "virman test silme");

        (await GetLedgerAsync(saved.VoucherId!.Value)).ShouldBeEmpty();
        (await GetLedgerAsync(twinBefore.VoucherId)).ShouldBeEmpty();
        (await GetTwinLineAsync(saved.LinkId!.Value, saved.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Transfer_to_same_or_missing_counter_account_is_rejected()
    {
        var data = await ArrangeCompanyAsync();

        // Karşı hesap = kaynak hesap → reddedilir.
        var same = await Should.ThrowAsync<BusinessException>(() => _voucherAppService.SaveLineAsync(
            VoucherTestLines.TransferLine(data, data.SubAccountId, ProcessDirectionType.Outbound, 100m)));
        same.Code.ShouldBe("TradeXpress:Voucher:TransferCounterSameAccount");

        // Karşı hesap seçilmemiş → reddedilir.
        var missingDto = VoucherTestLines.TransferLine(data, data.SubAccountId, ProcessDirectionType.Outbound, 100m);
        missingDto.CounterAccountId = null;
        var missing = await Should.ThrowAsync<BusinessException>(() => _voucherAppService.SaveLineAsync(missingDto));
        missing.Code.ShouldBe("TradeXpress:Voucher:TransferCounterRequired");
    }

    /// <summary>LinkId üzerinden ikiz satırı bulur (verilen satır hariç, silinmemiş).</summary>
    private Task<VoucherLine?> GetTwinLineAsync(Guid linkId, Guid excludedLineId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var q = await _voucherRepository.GetQueryableAsync();
            return q.SelectMany(v => v.Lines)
                .FirstOrDefault(l => l.LinkId == linkId && l.Id != excludedLineId && !l.IsDeleted);
        });
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
