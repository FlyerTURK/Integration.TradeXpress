using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Timing;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Orchestration;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Vouchers.Balance;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// REZERVASYON FİŞİ MATERYALİZASYONU — sipariş çekildiği anda madeni/mamülü müşteriye ayıran fişi yazar
/// (Faz 7; şablon <c>ConfirmationVoucherMaterializer</c>).
///
/// <para><b>Neden <c>VoucherAppService.SaveLineAsync</c> KULLANILMIYOR:</b> o yol <c>[Authorize]</c>'lıdır ve
/// çağıran-kullanıcının kasa yetkisini doğrular. Rezervasyon bir WORKER bağlamında (sipariş senkron döngüsü)
/// doğar — ortada kullanıcı yoktur. Aynı gerekçe teyit materyalizasyonunda da geçerliydi; desen oradan
/// devralındı. <b>Kapsam DAR tutulur:</b> bu sınıf yalnız <c>PaymentType=Reservation</c> satırı üretir.</para>
///
/// <para><b>Fiş MERKEZDE kesilir</b> (şube+kasa): sipariş şirketin tümüne aittir, hangi kasanın hazırlayacağı
/// henüz belli değildir. Hazırlayan kasa rezervasyonu fiziki çıkışa çevirirken kendi fişini keser.</para>
///
/// <para><b>Karşı taraf = kanalın varsayılan carisi</b> (<c>SalesChannelBase.SubAccountId</c>) — alan ZATEN
/// VARDI ve doc'u aynen bu kullanımı bekliyordu; sıfır migration.</para>
///
/// <para><b>⚠ <c>MainUnitId</c> FAIL-FAST:</b> stok raporu birimi olmayan satırı SESSİZCE atlar. Böyle bir
/// satır "0 gram rezerve eden hayalet" olurdu — rezervasyon var görünür, stok hiç düşmezdi. Bu yüzden
/// birimi çözülemeyen satır yazılmaz, sipariş <c>Blocked</c> olur ve gelen kutusuna düşer.</para>
/// </summary>
public class OrderReservationVoucherMaterializer : ITransientDependency
{
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly VoucherNumberAllocator _numberAllocator;
    private readonly BalanceLedgerSynchronizer _ledgerSynchronizer;
    private readonly VoucherCounterpartyResolver _counterpartyResolver;
    private readonly IGuidGenerator _guidGenerator;
    private readonly CommodityStockChangeQueuer _stockChangeQueuer;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public OrderReservationVoucherMaterializer(
        IRepository<Branch, Guid> branchRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        VoucherNumberAllocator numberAllocator,
        BalanceLedgerSynchronizer ledgerSynchronizer,
        VoucherCounterpartyResolver counterpartyResolver,
        IGuidGenerator guidGenerator,
        CommodityStockChangeQueuer stockChangeQueuer,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _branchRepository     = branchRepository;
        _vaultRepository      = vaultRepository;
        _subAccountRepository = subAccountRepository;
        _numberAllocator      = numberAllocator;
        _ledgerSynchronizer   = ledgerSynchronizer;
        _counterpartyResolver = counterpartyResolver;
        _guidGenerator        = guidGenerator;
        _stockChangeQueuer    = stockChangeQueuer;
        _asyncExecuter        = asyncExecuter;
    }

    /// <summary>Rezervasyon fişini yazar ve fiş + satır kimliklerini döner. Satır listesi BOŞ gelirse
    /// fiş AÇILMAZ (numarasız boş fiş üretmek defteri kirletir).</summary>
    public virtual async Task<OrderReservationVoucher> MaterializeAsync(
        Guid companyId,
        Guid? channelSubAccountId,
        IReadOnlyList<OrderReservationLine> lines,
        string? description)
    {
        if (lines.Count == 0)
        {
            throw new BusinessException("TradeXpress:OrderReservation:NoLines");
        }

        var (branchId, vaultId) = await ResolveHeadquartersAsync(companyId);

        // Karşı taraf: kanalın varsayılan carisi. Kanalda cari tanımlı değilse fiş başlıksız kalamaz →
        // fail-fast: sipariş Blocked olur ve kullanıcı kanala cari bağlar.
        if (channelSubAccountId is not { } subAccountId)
        {
            throw new BusinessException("TradeXpress:OrderReservation:ChannelSubAccountMissing");
        }

        // Üst cari alt hesaptan çözülür: kanal yalnız ALT hesabı taşır, fiş şeması ikisini de ister.
        var subAccount = await _subAccountRepository.FindAsync(subAccountId)
            ?? throw new BusinessException("TradeXpress:OrderReservation:ChannelSubAccountMissing");

        var counterparty = await _counterpartyResolver.ResolveCurrentAccountAsync(
            companyId, subAccount.AccountId, subAccountId);

        // İŞ TARİHİ wall-clock'tur (date-only semantik): Order.OrderDate UTC'dir, onu kullanmak fişi bir gün
        // kaydırabilirdi (CLAUDE.md §6 zaman kuralı).
        var voucher = new Voucher(
            companyId,
            branchId,
            vaultId,
            counterparty.AccountType,
            counterparty.AccountId,
            counterparty.AccountCode,
            counterparty.SubAccountId,
            counterparty.SubAccountCode,
            await _numberAllocator.NextNumberAsync(companyId),
            BusinessClock.Now().Date,
            description);

        var lineIds = new List<Guid>(lines.Count);
        foreach (var line in lines)
        {
            var dto = new VoucherLineDto
            {
                BranchId      = branchId,
                VaultId       = vaultId,
                AccountId     = counterparty.AccountId,
                SubAccountId  = counterparty.SubAccountId,
                Type          = line.Family,
                Direction     = ProcessDirectionType.Outbound,
                PaymentType   = ProcessPaymentType.Reservation,
                CommodityId   = line.CommodityId,
                CommodityCode = line.CommodityCode,
                VariantId     = line.CommodityVariantId,
                Quantity      = line.Quantity,
                Amount        = line.Amount,
                Factor        = line.Factor,
                Total         = line.Amount * line.Factor,
                MainUnitId    = line.MainUnitId,
                Description   = line.Description,
            };

            var added = voucher.AddLine(_guidGenerator.Create(), VoucherLineDtoFactory.ToLineInput(dto));
            lineIds.Add(added.Id);
        }

        await _numberAllocator.InsertNumberedAsync(voucher);

        // Ledger senkronu: rezervasyon posterlarda parasal etki üretmez (Reservation bacağı), ama zinciri
        // ATLAMAK yanlış olurdu — tip-agnostik motor kararı posterlara bırakır, burada varsayım yapılmaz.
        await _ledgerSynchronizer.SyncVoucherAsync(voucher);

        _stockChangeQueuer.QueueForVoucher(voucher);

        return new OrderReservationVoucher(voucher.Id, lineIds);
    }

    /// <summary>MERKEZ şube + o şubenin varsayılan kasası. SALT-OKUMA: <c>OrgTreeManager.EnsureHeadquarters*</c>
    /// yazan metotlardır ve sipariş senkron döngüsünde org ağacını değiştirmek istenmez — eksikse fail-fast.</summary>
    private async Task<(Guid BranchId, Guid VaultId)> ResolveHeadquartersAsync(Guid companyId)
    {
        var branch = await _asyncExecuter.FirstOrDefaultAsync(
            (await _branchRepository.GetQueryableAsync())
                .Where(b => b.CompanyId == companyId && b.IsHeadquarters));
        if (branch is null)
        {
            throw new BusinessException("TradeXpress:OrderReservation:HeadquartersMissing");
        }

        var vault = await _asyncExecuter.FirstOrDefaultAsync(
            (await _vaultRepository.GetQueryableAsync())
                .Where(v => v.BranchId == branch.Id)
                .OrderByDescending(v => v.IsDefault)
                .ThenBy(v => v.DisplayOrder));
        if (vault is null)
        {
            throw new BusinessException("TradeXpress:OrderReservation:HeadquartersVaultMissing");
        }

        return (branch.Id, vault.Id);
    }

}

/// <summary>Rezerve edilecek TEK emtia kalemi — reçete satırından × sipariş adedi türetilir.</summary>
public sealed record OrderReservationLine(
    ProcessType Family,
    Guid CommodityId,
    string CommodityCode,
    Guid? CommodityVariantId,
    decimal Quantity,
    decimal Amount,
    decimal Factor,
    Guid MainUnitId,
    string? Description);

/// <summary>Yazılan fişin kimliği + satır kimlikleri — bağ kayıtları (<see cref="OrderFulfillmentLink"/>)
/// bunlardan kurulur.</summary>
public sealed record OrderReservationVoucher(Guid VoucherId, IReadOnlyList<Guid> LineIds);
