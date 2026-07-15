using System;
using Integration.TradeXpress.Financials;
using Integration.TradeXpress.MultiCompany;
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
public class BalanceLedgerEntry : CreationAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    public virtual Guid? TenantId { get; protected set; }

    // ── Kapsam (voucher header'ından kopya — rapor scope filtresi) ──
    public virtual Guid CompanyId { get; protected set; }
    public virtual Guid BranchId { get; protected set; }
    public virtual Guid? VaultId { get; protected set; }

    /// <summary>Karşı taraf TİPİ (fiş başlığından) — karşı-taraf alanlarının ANLAMINI belirler; kasa
    /// bakiyeleri bu ayrımla, sahte cari üretilmeden ayrışır. Varsayılan
    /// <see cref="Vouchers.AccountType.CurrentAccount"/> (=0) → mevcut satırlar backfill'siz doğru.</summary>
    public virtual AccountType AccountType { get; protected set; }

    /// <summary>Karşı tarafın üst kimliği — <b>tipe göre polimorfik</b>: CurrentAccount → Account.Id ·
    /// Vault → Branch.Id. id-only snapshot (navigation/FK YOK — fiş başlığıyla aynı desen).</summary>
    public virtual Guid AccountId { get; protected set; }

    /// <summary>Üst kimliğin kod snapshot'ı (Account.Code ‖ Branch.Code).</summary>
    public virtual string AccountCode { get; protected set; } = string.Empty;

    /// <summary>Karşı tarafın alt kimliği — <b>tipe göre polimorfik</b>: CurrentAccount → SubAccount.Id ·
    /// Vault → Vault.Id. Bakiye/pozisyon okumaları daima bu alanla anahtarlanır.</summary>
    public virtual Guid SubAccountId { get; protected set; }

    /// <summary>Alt kimliğin kod snapshot'ı (SubAccount.Code ‖ Vault.Code).</summary>
    public virtual string SubAccountCode { get; protected set; } = string.Empty;

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

    /// <summary>Voucher.VoucherDate kopyası — wall-clock (kaymasız). <c>[DisableDateTimeNormalization]</c>
    /// ile ABP UTC normalizasyonundan muaf; kaynak Voucher zaten Kind=Unspecified verir → poster/ledger
    /// tarafında da gün-sınırı (pozisyon/ekstre) aynı wall-clock ile hizalı kalır.</summary>
    [DisableDateTimeNormalization]
    public virtual DateTime VoucherDate { get; protected set; }

    protected BalanceLedgerEntry()
    {
    }

    /// <summary>Voucher header (kapsam) + satır (sınıflandırma) + poster etkisinden (unit/amount) bir satır kurar.
    /// TenantId set EDİLMEZ — ABP IMultiTenant insert'te otomatik basar (Voucher ile aynı).
    /// <para><b>Rounding:</b> tutar KAYIT ANINDA N2 + AwayFromZero yuvarlanır (<see cref="FinancialRounding"/> —
    /// ERPPRO'da SQL kolon scale'inin fiili davranışı; poster ara hesapları HAM kalır, yalnız kalıcılaşan
    /// değer yuvarlanır).</para></summary>
    public BalanceLedgerEntry(Guid id, Voucher voucher, VoucherLine line, Guid unitId, decimal amount)
        : base(id)
    {
        CompanyId     = voucher.CompanyId;
        BranchId      = voucher.BranchId;
        VaultId       = voucher.VaultId;
        // Karşı taraf kapsamı fiş BAŞLIĞINDAN kopyalanır (tip + id'ler + kod snapshot'ları) — poster'lar
        // yalnız SATIRI okur; cari/kasa ayrımı buradan taşınır.
        AccountType    = voucher.AccountType;
        AccountId      = voucher.AccountId;
        AccountCode    = voucher.AccountCode;
        SubAccountId   = voucher.SubAccountId;
        SubAccountCode = voucher.SubAccountCode;
        UnitId        = unitId;
        Amount        = FinancialRounding.RoundAmount(amount);
        VoucherId     = voucher.Id;
        VoucherLineId = line.Id;
        ProcessType   = line.Type;
        Direction     = line.Direction;
        PaymentType   = line.PaymentType;
        VoucherNumber = voucher.VoucherNumber;
        VoucherDate   = voucher.VoucherDate;
    }
}
