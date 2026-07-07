using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.N11Products;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>N11 ürün listeleme mapping'i — ürün×kanal listelemesi (company-owned). Kategori attribute + Seyahat özel
/// bilgisi owned-collection → JSON kolonları; kimlik (SalesChannelId, ProductId) soft-delete filtreli benzersiz.</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureN11Products(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<N11ProductListing>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "N11ProductListings", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.CategoryExternalId).IsRequired().HasMaxLength(N11ProductConsts.ExternalIdMaxLength);
            b.Property(x => x.CategoryName).HasMaxLength(N11ProductConsts.CategoryNameMaxLength);
            b.Property(x => x.ShipmentTemplateName).IsRequired().HasMaxLength(N11ProductConsts.ShipmentTemplateNameMaxLength);
            b.Property(x => x.SaleStatus).HasMaxLength(N11ProductConsts.StatusMaxLength);
            b.Property(x => x.ApprovalStatus).HasMaxLength(N11ProductConsts.StatusMaxLength);
            b.Property(x => x.LastError).HasMaxLength(N11ProductConsts.LastErrorMaxLength);

            // Kategori attribute değerleri + Seyahat özel bilgisi → JSON kolonları (owned collection; N11'e push edilir, sorgulanmaz).
            b.OwnsMany(x => x.Attributes, a =>
            {
                a.ToJson();
                a.Property(p => p.Name).HasMaxLength(N11ProductConsts.AttributeNameMaxLength);
                a.Property(p => p.Value).HasMaxLength(N11ProductConsts.AttributeValueMaxLength);
            });
            b.OwnsMany(x => x.SpecialInfo, s =>
            {
                s.ToJson();
                s.Property(p => p.Key).HasMaxLength(N11ProductConsts.SpecialInfoKeyMaxLength);
                s.Property(p => p.Value).HasMaxLength(N11ProductConsts.SpecialInfoValueMaxLength);
            });

            // Kimlik: bir ürün bir kanalda TEK listeleme; soft-delete filtreli (silinen yeniden listelenebilsin).
            b.HasIndex(x => new { x.SalesChannelId, x.ProductId }).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
