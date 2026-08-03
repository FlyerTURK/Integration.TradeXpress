using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.ChannelQuestions;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>Kanal sorusu (ChannelQuestion) mapping'i — NÖTR müşteri sorusu (company-owned, per-tenant); TÜM
/// satış kanallarının ürün soruları TEK tabloda (kanal yalnız discriminator, kanal başına tablo YOK).
/// <c>Order</c> mapping'inin soru karşılığıdır: salt-okuma çekim + idempotent upsert, id-only referanslar,
/// snapshot metinleri. Cevap yereldedir, push henüz KAPALI (bkz. entity XML doc).</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureChannelQuestions(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<ChannelQuestion>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ChannelQuestions", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.RemoteQuestionId).IsRequired().HasMaxLength(ChannelQuestionConsts.RemoteQuestionIdMaxLength);
            b.Property(x => x.RemoteProductId).HasMaxLength(ChannelQuestionConsts.RemoteProductIdMaxLength);
            b.Property(x => x.ProductTitle).HasMaxLength(ChannelQuestionConsts.ProductTitleMaxLength);
            b.Property(x => x.Subject).HasMaxLength(ChannelQuestionConsts.SubjectMaxLength);
            b.Property(x => x.QuestionText).HasMaxLength(ChannelQuestionConsts.QuestionTextMaxLength);
            b.Property(x => x.CustomerName).HasMaxLength(ChannelQuestionConsts.CustomerNameMaxLength);
            b.Property(x => x.CustomerEmail).HasMaxLength(ChannelQuestionConsts.CustomerEmailMaxLength);
            b.Property(x => x.RemoteStatus).HasMaxLength(ChannelQuestionConsts.RemoteStatusMaxLength);
            b.Property(x => x.AnswerText).HasMaxLength(ChannelQuestionConsts.AnswerTextMaxLength);
            b.Property(x => x.AnswerPushError).HasMaxLength(ChannelQuestionConsts.AnswerPushErrorMaxLength);

            // NOT: SalesChannelId/ProductId için FK YOK — aggregate'ler arası bağ id-only (nav property yok, bu
            // yüzden EF konvansiyonu da FK üretmez; Order deseni). ProductId zaten silinebilir bir kaydı gösterir:
            // ürün gidince soru satırı snapshot alanlarıyla sağ kalmalı, FK bunu engellerdi.

            // İdempotency BEL KEMİĞİ: (SalesChannelId, RemoteQuestionId) tekil — ikinci çekim aynı soruyu bulup
            // GÜNCELLER, dublike üretmez. TenantId de anahtarda (kanal per-tenant zaten kapsar; simetri +
            // host/tenant izolasyonu). IsDeleted=0 filtresi ZORUNLU: soft-delete edilmiş satır uzak kimliği işgal
            // edip aynı sorunun yeniden çekilmesini engellemesin (Order/SalesChannel deseni).
            b.HasIndex(x => new { x.TenantId, x.SalesChannelId, x.RemoteQuestionId })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // Gelen kutusu listesi + SLA sıralaması: şirket içinde duruma göre süz, İLK GÖRÜLME'ye göre sırala
            // (geri sayım FirstSeenAt üzerinden — RemoteQuestionDate gün hassasiyetinde, bkz. entity XML doc).
            // Soldan-önek (TenantId, CompanyId) aynı zamanda ICompanyOwned güvenlik query-filter'ını da karşılar,
            // bu yüzden Order'daki gibi AYRI bir (TenantId, CompanyId) indeksi EKLENMEZ (yazma maliyeti bedava değil).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.NeutralStatus, x.FirstSeenAt });

            // Gönderim bekleyen kuyruğu — push açıldığında drenajın tarayacağı eksen; bugün "bekleyen cevap"
            // sayacını besler (ReadyToSend kuyruğu push kapalıyken BÜYÜR, görünür kalmalı).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.AnswerState });
        });
    }
}
