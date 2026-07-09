using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class N11Product_VariantReconcile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProductVariantId",
                table: "AppSalesChannelTrN11ProductVariantRecipeLines",
                newName: "OverrideHeaderId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductVariantRecipeLines_TenantId_SalesChannelTrN11ProductId_ProductVariantId_LineOrder",
                table: "AppSalesChannelTrN11ProductVariantRecipeLines",
                newName: "IX_AppSalesChannelTrN11ProductVariantRecipeLines_TenantId_SalesChannelTrN11ProductId_OverrideHeaderId_LineOrder");

            migrationBuilder.AddColumn<string>(
                name: "CombinationSignature",
                table: "AppSalesChannelTrN11ProductVariants",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11ProductVariants_TenantId_SalesChannelTrN11ProductId_CombinationSignature",
                table: "AppSalesChannelTrN11ProductVariants",
                columns: new[] { "TenantId", "SalesChannelTrN11ProductId", "CombinationSignature" },
                unique: true,
                filter: "[CombinationSignature] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannelTrN11ProductVariants_TenantId_SalesChannelTrN11ProductId_CombinationSignature",
                table: "AppSalesChannelTrN11ProductVariants");

            migrationBuilder.DropColumn(
                name: "CombinationSignature",
                table: "AppSalesChannelTrN11ProductVariants");

            migrationBuilder.RenameColumn(
                name: "OverrideHeaderId",
                table: "AppSalesChannelTrN11ProductVariantRecipeLines",
                newName: "ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductVariantRecipeLines_TenantId_SalesChannelTrN11ProductId_OverrideHeaderId_LineOrder",
                table: "AppSalesChannelTrN11ProductVariantRecipeLines",
                newName: "IX_AppSalesChannelTrN11ProductVariantRecipeLines_TenantId_SalesChannelTrN11ProductId_ProductVariantId_LineOrder");
        }
    }
}
