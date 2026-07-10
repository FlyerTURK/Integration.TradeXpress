using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Trendyol_StockItem_TableRenames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSalesChannelTrTrendyolProductVariants",
                table: "AppSalesChannelTrTrendyolProductVariants");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSalesChannelTrTrendyolProductVariantRecipeLines",
                table: "AppSalesChannelTrTrendyolProductVariantRecipeLines");

            migrationBuilder.RenameTable(
                name: "AppSalesChannelTrTrendyolProductVariants",
                newName: "AppSalesChannelTrTrendyolProductStockItems");

            migrationBuilder.RenameTable(
                name: "AppSalesChannelTrTrendyolProductVariantRecipeLines",
                newName: "AppSalesChannelTrTrendyolProductStockItemRecipeLines");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrTrendyolProductVariants_TenantId_SalesChannelTrTrendyolProductId_ProductVariantId",
                table: "AppSalesChannelTrTrendyolProductStockItems",
                newName: "IX_AppSalesChannelTrTrendyolProductStockItems_TenantId_SalesChannelTrTrendyolProductId_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrTrendyolProductVariants_TenantId_SalesChannelTrTrendyolProductId_CombinationSignature",
                table: "AppSalesChannelTrTrendyolProductStockItems",
                newName: "IX_AppSalesChannelTrTrendyolProductStockItems_TenantId_SalesChannelTrTrendyolProductId_CombinationSignature");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrTrendyolProductVariants_TenantId_CompanyId",
                table: "AppSalesChannelTrTrendyolProductStockItems",
                newName: "IX_AppSalesChannelTrTrendyolProductStockItems_TenantId_CompanyId");

            migrationBuilder.RenameColumn(
                name: "ProductVariantId",
                table: "AppSalesChannelTrTrendyolProductStockItemRecipeLines",
                newName: "StockItemId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrTrendyolProductVariantRecipeLines_TenantId_SalesChannelTrTrendyolProductId_ProductVariantId_LineOrder",
                table: "AppSalesChannelTrTrendyolProductStockItemRecipeLines",
                newName: "IX_AppSalesChannelTrTrendyolProductStockItemRecipeLines_TenantId_SalesChannelTrTrendyolProductId_StockItemId_LineOrder");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrTrendyolProductVariantRecipeLines_TenantId_CompanyId",
                table: "AppSalesChannelTrTrendyolProductStockItemRecipeLines",
                newName: "IX_AppSalesChannelTrTrendyolProductStockItemRecipeLines_TenantId_CompanyId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSalesChannelTrTrendyolProductStockItems",
                table: "AppSalesChannelTrTrendyolProductStockItems",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSalesChannelTrTrendyolProductStockItemRecipeLines",
                table: "AppSalesChannelTrTrendyolProductStockItemRecipeLines",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSalesChannelTrTrendyolProductStockItems",
                table: "AppSalesChannelTrTrendyolProductStockItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSalesChannelTrTrendyolProductStockItemRecipeLines",
                table: "AppSalesChannelTrTrendyolProductStockItemRecipeLines");

            migrationBuilder.RenameTable(
                name: "AppSalesChannelTrTrendyolProductStockItems",
                newName: "AppSalesChannelTrTrendyolProductVariants");

            migrationBuilder.RenameTable(
                name: "AppSalesChannelTrTrendyolProductStockItemRecipeLines",
                newName: "AppSalesChannelTrTrendyolProductVariantRecipeLines");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrTrendyolProductStockItems_TenantId_SalesChannelTrTrendyolProductId_ProductVariantId",
                table: "AppSalesChannelTrTrendyolProductVariants",
                newName: "IX_AppSalesChannelTrTrendyolProductVariants_TenantId_SalesChannelTrTrendyolProductId_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrTrendyolProductStockItems_TenantId_SalesChannelTrTrendyolProductId_CombinationSignature",
                table: "AppSalesChannelTrTrendyolProductVariants",
                newName: "IX_AppSalesChannelTrTrendyolProductVariants_TenantId_SalesChannelTrTrendyolProductId_CombinationSignature");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrTrendyolProductStockItems_TenantId_CompanyId",
                table: "AppSalesChannelTrTrendyolProductVariants",
                newName: "IX_AppSalesChannelTrTrendyolProductVariants_TenantId_CompanyId");

            migrationBuilder.RenameColumn(
                name: "StockItemId",
                table: "AppSalesChannelTrTrendyolProductVariantRecipeLines",
                newName: "ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrTrendyolProductStockItemRecipeLines_TenantId_SalesChannelTrTrendyolProductId_StockItemId_LineOrder",
                table: "AppSalesChannelTrTrendyolProductVariantRecipeLines",
                newName: "IX_AppSalesChannelTrTrendyolProductVariantRecipeLines_TenantId_SalesChannelTrTrendyolProductId_ProductVariantId_LineOrder");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrTrendyolProductStockItemRecipeLines_TenantId_CompanyId",
                table: "AppSalesChannelTrTrendyolProductVariantRecipeLines",
                newName: "IX_AppSalesChannelTrTrendyolProductVariantRecipeLines_TenantId_CompanyId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSalesChannelTrTrendyolProductVariants",
                table: "AppSalesChannelTrTrendyolProductVariants",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSalesChannelTrTrendyolProductVariantRecipeLines",
                table: "AppSalesChannelTrTrendyolProductVariantRecipeLines",
                column: "Id");
        }
    }
}
