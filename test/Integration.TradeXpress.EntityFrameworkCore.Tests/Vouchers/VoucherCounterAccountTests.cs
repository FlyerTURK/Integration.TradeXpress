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
/// ÇİFT-BACAK (karşı hesap) ağı — 2026-07-26'da karşı hesap virman DIŞINDAKİ tiplere de açıldı
/// (kargo gideri: kanal fişinde hizmet çıkışı ↔ kargo firmasının fişinde giriş). Bu dosya iki şeyi korur:
/// <list type="number">
///   <item><b>Virman REGRESYONU</b>: değişiklikten sonra da virman aynen çalışmalı — karşı hesaba yeni fiş
///   açılır, ters yönlü ayna satır yazılır, iki bakiye simetrik doğar, açıklamada kaynak kodu geçer.</item>
///   <item><b>Yeni davranış</b>: hizmet satırı karşı hesapla kaydedilince aynı ayna kurulur; karşı hesap
///   BOŞ bırakılırsa ayna fiş AÇILMAZ (tek bacak).</item>
/// </list>
/// Mock yok — üretim yolu (<c>SaveLineAsync</c>) çalıştırılır.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class VoucherCounterAccountTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IVoucherAppService _voucherAppService;
    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledgerRepository;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public VoucherCounterAccountTests()
    {
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _voucherRepository = GetRequiredService<IRepository<Voucher, Guid>>();
        _ledgerRepository  = GetRequiredService<IRepository<BalanceLedgerEntry, Guid>>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
    }

    /// <summary>REGRESYON: virman ikizi bozulmadı — iki ayrı fiş, ters yönlü satırlar, simetrik bakiye.</summary>
    [Fact]
    public async Task Transfer_still_creates_mirrored_counter_voucher()
    {
        var data      = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("CTA1"));
        _companyContext.CompanyId = data.CompanyId;
        var counterId = await WithUnitOfWorkAsync(() => _seeder.SeedCounterSubAccountAsync(data));

        await _voucherAppService.SaveLineAsync(
            VoucherTestLines.TransferLine(data, counterId, ProcessDirectionType.Outbound, 500m));

        var snapshot = await LoadSnapshotAsync(data.CompanyId);

        // İki fiş: kaynak carinin fişi + karşı hesabın KENDİ fişi.
        snapshot.Count.ShouldBe(2);
        snapshot.ShouldContain(v => v.SubAccountId == data.SubAccountId);
        snapshot.ShouldContain(v => v.SubAccountId == counterId);

        // Ayna satır: yön TERS, karşı referans kaynağa döner, LinkId ortak.
        var primaryLine = snapshot.Single(v => v.SubAccountId == data.SubAccountId).Lines.Single();
        var twinLine    = snapshot.Single(v => v.SubAccountId == counterId).Lines.Single();

        primaryLine.Direction.ShouldBe(ProcessDirectionType.Outbound);
        twinLine.Direction.ShouldBe(ProcessDirectionType.Inbound);
        twinLine.CounterAccountId.ShouldBe(data.SubAccountId);
        twinLine.LinkId.ShouldBe(primaryLine.LinkId);
        twinLine.PayTotal.ShouldBe(primaryLine.PayTotal);

        // Bakiye simetrisi: −500 / +500.
        var ledger = await WithUnitOfWorkAsync(
            () => _ledgerRepository.GetListAsync(e => e.CompanyId == data.CompanyId));
        ledger.Count.ShouldBe(2);
        ledger.Sum(e => e.Amount).ShouldBe(0m);
    }

    /// <summary>YENİ: hizmet satırı karşı hesapla → aynı ayna kurulur (kargo gideri bu yolla işler).</summary>
    [Fact]
    public async Task Service_line_with_counter_account_creates_mirrored_voucher()
    {
        var data      = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("CTA2"));
        _companyContext.CompanyId = data.CompanyId;
        var counterId = await WithUnitOfWorkAsync(() => _seeder.SeedCounterSubAccountAsync(data));

        var line = VoucherTestLines.CashLine(data, ProcessDirectionType.Outbound, 120m);
        line.Type             = ProcessType.Service;
        line.CounterAccountId = counterId;

        await _voucherAppService.SaveLineAsync(line);

        var snapshot = await LoadSnapshotAsync(data.CompanyId);

        snapshot.Count.ShouldBe(2);
        var twin = snapshot.Single(v => v.SubAccountId == counterId).Lines.Single();
        twin.Type.ShouldBe(ProcessType.Service);                    // ayna TİPİ korur
        twin.Direction.ShouldBe(ProcessDirectionType.Inbound);      // yön ters
        twin.CounterAccountId.ShouldBe(data.SubAccountId);          // karşı referans kaynağa döner

        // Karşı fişte KAYNAK carinin kodu görünür (açıklama "{karşı}/{kaynak}:..." biçiminde ters çevrilir).
        twin.Description.ShouldNotBeNullOrWhiteSpace();

        var ledger = await WithUnitOfWorkAsync(
            () => _ledgerRepository.GetListAsync(e => e.CompanyId == data.CompanyId));
        ledger.Count.ShouldBe(2);
        ledger.Sum(e => e.Amount).ShouldBe(0m);
    }

    /// <summary>Karşı hesap BOŞ hizmet satırı → ayna fiş AÇILMAZ (tek bacak; eski davranış korunur).</summary>
    [Fact]
    public async Task Service_line_without_counter_account_stays_single_legged()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("CTA3"));
        _companyContext.CompanyId = data.CompanyId;

        var line = VoucherTestLines.CashLine(data, ProcessDirectionType.Outbound, 120m);
        line.Type = ProcessType.Service;
        // CounterAccountId BOŞ

        await _voucherAppService.SaveLineAsync(line);

        var snapshot = await LoadSnapshotAsync(data.CompanyId);

        snapshot.Count.ShouldBe(1);                                  // yalnız kaynak fişi
        snapshot.Single().Lines.Single().CounterAccountId.ShouldBeNull();
    }

    /// <summary>Fişleri satırlarıyla birlikte UoW İÇİNDE belleğe kopyalar — aggregate koleksiyonu
    /// UoW kapandıktan sonra boş görünüyor, bu yüzden doğrulama verisi burada dondurulur.</summary>
    private async Task<List<VoucherSnapshot>> LoadSnapshotAsync(Guid companyId)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            // WithDetailsAsync ŞART: includeDetails bu depoda satırları getirmiyor (koleksiyon boş gelir).
            var query = await _voucherRepository.WithDetailsAsync(v => v.Lines);
            return query
                .Where(v => v.CompanyId == companyId)
                .ToList()
                .Select(v => new VoucherSnapshot(v.SubAccountId, v.Lines.ToList()))
                .ToList();
        });
    }

    private sealed record VoucherSnapshot(Guid? SubAccountId, List<VoucherLine> Lines);
}
