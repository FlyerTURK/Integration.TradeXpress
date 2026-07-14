using Integration.TradeXpress.SpecialCodes;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Integration.TradeXpress.EntityFrameworkCore;

public static class TradeXpressDbContextModelCreatingExtensionsSpecialCodes
{
    public static void ConfigureSpecialCodes(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<SpecialCode>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SpecialCodes", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(SpecialCodeConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(SpecialCodeConsts.NameMaxLength);
            b.Property(x => x.EntityName).IsRequired().HasMaxLength(SpecialCodeConsts.EntityNameMaxLength);
            b.Property(x => x.PropertyName).IsRequired().HasMaxLength(SpecialCodeConsts.PropertyNameMaxLength);
            b.Property(x => x.Description).HasMaxLength(SpecialCodeConsts.DescriptionMaxLength);

            // Benzersizlik: bir kod, bir bağlamda (şirket + entity + property) tektir.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.EntityName, x.PropertyName, x.Code }).IsUnique();
            // Picker bağlam sorgusu (EntityName+PropertyName) için ikincil indeks.
            b.HasIndex(x => new { x.EntityName, x.PropertyName });

            // Hiyerarşi — self-FK (Restrict: alt kodu olan silinmeden parent silinemez).
            b.HasOne<SpecialCode>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
