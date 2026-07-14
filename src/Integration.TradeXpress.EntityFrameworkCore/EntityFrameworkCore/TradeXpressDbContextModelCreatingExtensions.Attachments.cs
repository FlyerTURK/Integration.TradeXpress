using Integration.TradeXpress.Attachments;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Integration.TradeXpress.EntityFrameworkCore;

public static class TradeXpressDbContextModelCreatingExtensionsAttachments
{
    public static void ConfigureEntityImages(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<EntityImage>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "EntityImages", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.EntityName).IsRequired().HasMaxLength(EntityImageConsts.EntityNameMaxLength);
            b.Property(x => x.Url).HasMaxLength(EntityImageConsts.UrlMaxLength);
            b.Property(x => x.BlobName).HasMaxLength(EntityImageConsts.BlobNameMaxLength);
            b.Property(x => x.FileName).HasMaxLength(EntityImageConsts.FileNameMaxLength);

            // Sahip-kayıt sorgusu (EntityName + EntityId) + sıra — picker/GetFor bağlam sorgusu.
            b.HasIndex(x => new { x.EntityName, x.EntityId, x.DisplayOrder });
        });
    }

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
}
