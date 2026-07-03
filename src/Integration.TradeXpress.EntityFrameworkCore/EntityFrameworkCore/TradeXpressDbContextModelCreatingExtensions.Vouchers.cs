using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Cari/fiş alanı mapping'leri: hesaplar, fişler, bakiye ledger'ı ve bilanço snapshot'ları.
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureAccounts(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Account>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Accounts", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(AccountConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(AccountConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(AccountConsts.DescriptionMaxLength);
            b.Property(x => x.Limit).HasPrecision(18, 2);

            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });

            // Para birimi referansları (cins + limit) — ZORUNLU; hesap varken birim silinemez (Restrict).
            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>()
                .WithMany()
                .HasForeignKey(x => x.BalanceCurrencyUnitId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>()
                .WithMany()
                .HasForeignKey(x => x.LimitUnitId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SubAccount>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SubAccounts", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(AccountConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(AccountConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(AccountConsts.DescriptionMaxLength);

            b.HasIndex(x => new { x.TenantId, x.AccountId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.BranchId });

            // Parent hesap (ZORUNLU) + şube (OPSİYONEL/nullable) — id-only (nav YOK); referans varken silme engeli (Restrict).
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public static void ConfigureVouchers(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Voucher>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Vouchers", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Description).HasMaxLength(VoucherConsts.DescriptionMaxLength);
            b.Property(x => x.VoucherNumber).IsRequired();
            b.Property(x => x.VoucherDate).IsRequired();

            // Fiş numarası şirket bazında tekil.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.VoucherNumber }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.BranchId });
            b.HasIndex(x => new { x.TenantId, x.AccountId });
            // Perf (keşif turu 2, K3): TÜM raporlar CompanyId+VoucherDate, TÜM cari sorguları
            // CompanyId+SubAccountId(+tarih) filtreler — bunlar index'siz company-scan'e düşüyordu.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.VoucherDate });
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.SubAccountId, x.VoucherDate });

            // FK'lar — referans varken kaynak silinemez (Restrict).
            b.HasOne<Companies.Company>().WithMany()
                .HasForeignKey(x => x.CompanyId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Branches.Branch>().WithMany()
                .HasForeignKey(x => x.BranchId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Vaults.Vault>().WithMany()
                .HasForeignKey(x => x.VaultId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Accounts.Account>().WithMany()
                .HasForeignKey(x => x.AccountId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Accounts.SubAccount>().WithMany()
                .HasForeignKey(x => x.SubAccountId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(x => x.Lines).WithOne(l => l.Voucher).HasForeignKey(l => l.VoucherId)
                .IsRequired().OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VoucherLine>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "VoucherLines", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.CommodityCode).HasMaxLength(VoucherConsts.CommodityCodeMaxLength);
            b.Property(x => x.PayCommodityCode).HasMaxLength(VoucherConsts.CommodityCodeMaxLength);
            b.Property(x => x.Description).HasMaxLength(VoucherConsts.DescriptionMaxLength);

            // N5: milyem / çarpan / parite / fiyat / miktar
            b.Property(x => x.Quantity).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);
            b.Property(x => x.Factor).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);
            b.Property(x => x.PayFactor).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);
            b.Property(x => x.MarketPrice).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);
            b.Property(x => x.PayUnitRate).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);

            // Yan metal milyem hassasiyeti — 0.008 gibi değerler default (18,2)'de 0.01'e yuvarlanıyordu
            // (canlı bug; AU Factor zaten N5 konfigürlüydü, yan metaller unutulmuştu).
            b.Property(x => x.SilverFactor).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);
            b.Property(x => x.PlatinumFactor).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);
            b.Property(x => x.PalladiumFactor).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);

            // N2: para / has miktarları
            b.Property(x => x.Amount).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.Total).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.PayTotal).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.Profit).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);

            b.HasIndex(x => x.VoucherId);

            // Virman ikiz araması: LinkId (legacy RefNo) ile zıt bacak bulunur (güncelle/sil senkronu).
            b.HasIndex(x => x.LinkId);
        });

    }

    /// <summary>
    /// Bakiye ledger'ı (poster çıktısının kalıcı kaydı) — pozisyon raporu bunu GROUP BY/SUM ile okur.
    /// FK YOK: VoucherId mantıksal referans (id-only desen); senkron app-katmanında (BalanceLedgerSynchronizer).
    /// </summary>
    public static void ConfigureBalanceLedger(this ModelBuilder builder)
    {
        builder.Entity<Integration.TradeXpress.Vouchers.Balance.BalanceLedgerEntry>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "BalanceLedgerEntries", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // İşaretli net etki — N2 (Voucher tutarlarıyla aynı hassasiyet).
            b.Property(x => x.Amount).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);

            // Rapor: scope + birim bazında GROUP BY/SUM (kapsayan index — DB-tarafı toplam hızlı).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.BranchId, x.UnitId });
            // Senkron: voucher bazında sil + yeniden yaz.
            b.HasIndex(x => x.VoucherId);
        });
    }

    /// <summary>
    /// Bilanço snapshot'ları (dondurulmuş kategori×birim satırları) — ERPPRO <c>Bilanco.Bilancolar</c> paritesi.
    /// FK YOK: CompanyId/BranchId/UnitId/BaseUnitId id-only mantıksal referans (ledger deseni). SaveAsync idempotent
    /// (Scope, CompanyId, BranchId, AsOfDate) bazında sil+yeniden yaz; index o sorguyu + gün-serisi okumasını hızlandırır.
    /// </summary>
    public static void ConfigureBalanceSheetSnapshots(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Integration.TradeXpress.Reports.BalanceSheet.BalanceSheetSnapshot>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "BalanceSheetSnapshots", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Category).IsRequired()
                .HasMaxLength(Integration.TradeXpress.Reports.BalanceSheet.BalanceSheetSnapshotConsts.CategoryMaxLength);
            b.Property(x => x.BaseCurrencyCode).IsRequired()
                .HasMaxLength(Integration.TradeXpress.Reports.BalanceSheet.BalanceSheetSnapshotConsts.BaseCurrencyCodeMaxLength);

            // N2 (Voucher tutarlarıyla aynı) miktar/net; N5 (kur çaprazı hassasiyeti) donmuş değerleme kuru.
            b.Property(x => x.Amount).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.Net).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.ValuationRate).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);

            // SaveAsync sil+yeniden-yaz + gün-serisi okuması: (kapsam + tarih) kapsayan sorgu index'i.
            b.HasIndex(x => new { x.TenantId, x.Scope, x.CompanyId, x.BranchId, x.AsOfDate });
        });
    }
}
