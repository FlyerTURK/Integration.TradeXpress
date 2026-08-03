using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.ChannelQuestions;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>Kanal sorusu SENKRON DEFTERİ (<see cref="ChannelQuestionSyncState"/>) mapping'i — kanal başına TEK
/// satır; çekimin "nerede kaldım" ilerlemesi (gerekçe entity XML doc'unda). Küçük ve sık yazılan bir tablodur:
/// tek benzersizlik indeksi dışında indeks EKLENMEZ (yazma maliyeti bedava değil, satır sayısı = kanal sayısı).</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureChannelQuestionSyncStates(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<ChannelQuestionSyncState>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ChannelQuestionSyncStates", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // NOT: SalesChannelId için FK YOK — aggregate'ler arası bağ id-only (ChannelQuestion deseni).
            // Kanal silinirse defter satırı öksüz kalır; zararsızdır (bir daha okunmaz) ve FK, kanal silmeyi
            // bloklayarak kullanıcıyı makine defteri yüzünden cezalandırırdı.

            // Kanal başına TEK defter. IsDeleted filtresi YOK: entity soft-delete taşımıyor (bkz. entity XML doc)
            // — filtreli indeks yazmak, olmayan bir kolona bağımlılık yaratırdı.
            b.HasIndex(x => new { x.TenantId, x.SalesChannelId }).IsUnique();
        });
    }
}
