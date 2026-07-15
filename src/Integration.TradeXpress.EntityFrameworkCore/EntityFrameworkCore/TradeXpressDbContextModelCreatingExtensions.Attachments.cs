using Integration.TradeXpress.Attachments;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Integration.TradeXpress.EntityFrameworkCore;

public static class TradeXpressDbContextModelCreatingExtensionsAttachments
{
    public static void ConfigureEntityDocuments(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<EntityDocument>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "EntityDocuments", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.EntityName).IsRequired().HasMaxLength(EntityDocumentConsts.EntityNameMaxLength);
            b.Property(x => x.FileName).IsRequired().HasMaxLength(EntityDocumentConsts.FileNameMaxLength);
            b.Property(x => x.BlobName).IsRequired().HasMaxLength(EntityDocumentConsts.BlobNameMaxLength);
            b.Property(x => x.ContentType).IsRequired().HasMaxLength(EntityDocumentConsts.ContentTypeMaxLength);
            b.Property(x => x.Description).HasMaxLength(EntityDocumentConsts.DescriptionMaxLength);

            // Sahip-kayıt sorgusu (tenant + EntityName + EntityId) + sıra — GetFor bağlam sorgusu.
            b.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId, x.DisplayOrder });
        });
    }

    public static void ConfigureEntityNotes(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<EntityNote>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "EntityNotes", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.EntityName).IsRequired().HasMaxLength(EntityNoteConsts.EntityNameMaxLength);
            b.Property(x => x.Title).HasMaxLength(EntityNoteConsts.TitleMaxLength);
            b.Property(x => x.Text).IsRequired().HasMaxLength(EntityNoteConsts.TextMaxLength);

            // Sahip-kayıt sorgusu (tenant + EntityName + EntityId) + sıra — GetFor bağlam sorgusu.
            b.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId, x.DisplayOrder });
        });
    }

    // Merkezi medya kütüphanesi (DAM) — Media (self-contained blob) + EntityMediaLink (entity→media referansı).
    public static void ConfigureMedia(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Media>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Media", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.BlobName).IsRequired().HasMaxLength(MediaConsts.BlobNameMaxLength);
            b.Property(x => x.PosterBlobName).HasMaxLength(MediaConsts.BlobNameMaxLength);
            b.Property(x => x.FileName).IsRequired().HasMaxLength(MediaConsts.FileNameMaxLength);
            b.Property(x => x.ContentType).IsRequired().HasMaxLength(MediaConsts.ContentTypeMaxLength);
            b.Property(x => x.ContentHash).IsRequired().HasMaxLength(MediaConsts.ContentHashMaxLength);

            // Company içinde içerik-hash dedup + kütüphane listeleme sorgusu (tenant + company + hash).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.ContentHash });

            // Klasöre göre kütüphane filtresi.
            b.HasIndex(x => x.FolderId);
        });

        builder.Entity<MediaFolder>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "MediaFolders", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(MediaConsts.FolderNameMaxLength);

            // Company klasör ağacı + üst-klasör çocukları sorgusu.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.ParentId });
        });

        builder.Entity<EntityMediaLink>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "EntityMediaLinks", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.EntityName).IsRequired().HasMaxLength(MediaConsts.EntityNameMaxLength);

            // Sahip-kayıt link sorgusu (EntityName + EntityId) + sıra — GetFor bağlam sorgusu.
            b.HasIndex(x => new { x.EntityName, x.EntityId, x.DisplayOrder });
        });
    }
}
