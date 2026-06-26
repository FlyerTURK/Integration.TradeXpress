using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Bir voucher satırının BİRİM-bazında bakiye etkisinin KALICI kaydı (poster çıktısı persist edilir).
/// Pozisyon raporu bunu <c>GROUP BY UnitId + SUM</c> ile okur (rapor-zamanı yeniden hesaplama yok → jet hız).
/// Bir <see cref="VoucherLine"/> → 0..N <see cref="BalanceEffect"/> → 0..N ledger satırı.
///
/// <para><b>Kural HARDCODE edilmez</b> — etki tamamen ilgili <c>IVoucherLineBalancePoster</c>'dan gelir;
/// ledger yalnız o çıktıyı saklar. Senkron: voucher save/update/delete'te VoucherId bazında
/// <b>sil + yeniden yaz</b> (<c>BalanceLedgerSynchronizer</c>) → kaydedilen işlemle inşaen tutarlı.</para>
/// </summary>
public class BalanceLedgerEntry : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    // ── Kapsam (voucher header'ından kopya — rapor scope filtresi) ──
    public virtual Guid CompanyId { get; protected set; }
    public virtual Guid BranchId { get; protected set; }
    public virtual Guid? VaultId { get; protected set; }
    public virtual Guid AccountId { get; protected set; }
    public virtual Guid? SubAccountId { get; protected set; }

    // ── Bakiye etkisi (poster çıktısı) ──
    /// <summary>Etkilenen para/emtia birimi (BalanceEffect.UnitId).</summary>
    public virtual Guid UnitId { get; protected set; }

    /// <summary>İşaretli net etki — + ALACAK, − BORÇ (BalanceEffect.Amount).</summary>
    public virtual decimal Amount { get; protected set; }

    // ── Kaynak (senkron + drill + audit) ──
    public virtual Guid VoucherId { get; protected set; }
    public virtual Guid VoucherLineId { get; protected set; }
    public virtual ProcessType ProcessType { get; protected set; }
    public virtual ProcessDirectionType Direction { get; protected set; }
    public virtual ProcessPaymentType? PaymentType { get; protected set; }
    public virtual long VoucherNumber { get; protected set; }
    public virtual DateTime VoucherDate { get; protected set; }

    protected BalanceLedgerEntry()
    {
    }

    /// <summary>Voucher header (kapsam) + satır (sınıflandırma) + poster etkisinden (unit/amount) bir satır kurar.
    /// TenantId set EDİLMEZ — ABP IMultiTenant insert'te otomatik basar (Voucher ile aynı).</summary>
    public BalanceLedgerEntry(Guid id, Voucher voucher, VoucherLine line, Guid unitId, decimal amount)
        : base(id)
    {
        CompanyId     = voucher.CompanyId;
        BranchId      = voucher.BranchId;
        VaultId       = voucher.VaultId;
        AccountId     = voucher.AccountId;
        SubAccountId  = voucher.SubAccountId;
        UnitId        = unitId;
        Amount        = amount;
        VoucherId     = voucher.Id;
        VoucherLineId = line.Id;
        ProcessType   = line.Type;
        Direction     = line.Direction;
        PaymentType   = line.PaymentType;
        VoucherNumber = voucher.VoucherNumber;
        VoucherDate   = voucher.VoucherDate;
    }
}
