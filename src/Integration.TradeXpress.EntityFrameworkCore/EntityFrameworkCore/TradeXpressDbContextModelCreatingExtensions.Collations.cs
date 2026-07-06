using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.AssayOffices;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Accounts;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Kod kolonu collation politikası. C# tarafında kod-normalizasyonu <c>ToUpperInvariant</c> (ordinal)
/// ile yapılır (Türkçe İ/i kaçağı governance testiyle kapalı: <c>StringNormalizationCultureTests</c>).
/// DB benzersizlik karşılaştırması da ordinal/binary katlamalı olmalı; aksi halde kültür-duyarlı bir
/// kolon collation'ı (ör. Turkish_CI 'İ'≠'I') C#'ın eşit saydığı iki kodu farklı görüp benzersizlik
/// garantisini deler. <see cref="TradeXpressDbContextModelCreatingExtensions.ApplyCodeColumnCollations"/>
/// bu collation'ı benzersizliğe giren Code kolonlarına uygular (YALNIZ SQL Server; çağrı yeri korur).
/// </summary>
public static class TradeXpressCollations
{
    /// <summary>
    /// Deterministik binary collation — kod-noktası (ordinal) sıralar/karşılaştırır, C# <c>ToUpperInvariant</c>
    /// ile birebir hizalanır. SQL-Server-özel; Sqlite tanımaz (test tarafında default <c>BINARY</c> zaten ordinal).
    /// </summary>
    public const string OrdinalCode = "Latin1_General_100_BIN2";
}

public static partial class TradeXpressDbContextModelCreatingExtensions
{
    /// <summary>
    /// Benzersizliğe giren Code kolonlarına ordinal (BIN2) collation verir. YALNIZ SQL Server provider'ında
    /// çağrılmalı (<c>Database.IsSqlServer()</c> ile korunur) — Sqlite <see cref="TradeXpressCollations.OrdinalCode"/>'u
    /// tanımaz ve karşılaştırmada "no such collation" hatası verir. Voucher DIŞARIDA: benzersizlik anahtarı
    /// <c>VoucherNumber</c> (long/sayısal) olduğundan collation uygulanamaz.
    /// </summary>
    public static void ApplyCodeColumnCollations(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        ApplyOrdinalCodeCollation<CurrencyUnit>(builder);
        ApplyOrdinalCodeCollation<Company>(builder);
        ApplyOrdinalCodeCollation<Country>(builder);
        ApplyOrdinalCodeCollation<Branch>(builder);
        ApplyOrdinalCodeCollation<Vault>(builder);
        ApplyOrdinalCodeCollation<AssayOffice>(builder);
        ApplyOrdinalCodeCollation<Cash>(builder);
        ApplyOrdinalCodeCollation<Service>(builder);
        // SalesChannel TPT: Code kolonu SOYUT TABAN tablosunda (AppSalesChannels) → collation base'e uygulanır.
        ApplyOrdinalCodeCollation<SalesChannelBase>(builder);
        ApplyOrdinalCodeCollation<Future>(builder);
        ApplyOrdinalCodeCollation<Scrap>(builder);
        ApplyOrdinalCodeCollation<Metal>(builder);
        ApplyOrdinalCodeCollation<Stone>(builder);
        ApplyOrdinalCodeCollation<Jewelry>(builder);
        ApplyOrdinalCodeCollation<Account>(builder);
        ApplyOrdinalCodeCollation<SubAccount>(builder);
    }

    // Tüm kapsam entity'lerinde kolon adı birebir "Code" — tek yardımcı ile SSOT (magic string tek yerde).
    private static void ApplyOrdinalCodeCollation<TEntity>(ModelBuilder builder)
        where TEntity : class
    {
        builder.Entity<TEntity>().Property(EntityFieldConsts.CodePropertyName).UseCollation(TradeXpressCollations.OrdinalCode);
    }
}
