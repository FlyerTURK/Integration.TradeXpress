using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class N11Product_Rename_Attribute_StockItem_Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductVariants",
                table: "AppSalesChannelTrN11ProductVariants");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductVariantRecipeLines",
                table: "AppSalesChannelTrN11ProductVariantRecipeLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductAttributeAxisValues",
                table: "AppSalesChannelTrN11ProductAttributeAxisValues");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductAttributeAxes",
                table: "AppSalesChannelTrN11ProductAttributeAxes");

            migrationBuilder.RenameTable(
                name: "AppSalesChannelTrN11ProductVariants",
                newName: "AppSalesChannelTrN11ProductStockItems");

            migrationBuilder.RenameTable(
                name: "AppSalesChannelTrN11ProductVariantRecipeLines",
                newName: "AppSalesChannelTrN11ProductStockItemRecipeLines");

            migrationBuilder.RenameTable(
                name: "AppSalesChannelTrN11ProductAttributeAxisValues",
                newName: "AppSalesChannelTrN11ProductAttributeValues");

            migrationBuilder.RenameTable(
                name: "AppSalesChannelTrN11ProductAttributeAxes",
                newName: "AppSalesChannelTrN11ProductAttributes");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductVariants_TenantId_SalesChannelTrN11ProductId_ProductVariantId",
                table: "AppSalesChannelTrN11ProductStockItems",
                newName: "IX_AppSalesChannelTrN11ProductStockItems_TenantId_SalesChannelTrN11ProductId_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductVariants_TenantId_SalesChannelTrN11ProductId_CombinationSignature",
                table: "AppSalesChannelTrN11ProductStockItems",
                newName: "IX_AppSalesChannelTrN11ProductStockItems_TenantId_SalesChannelTrN11ProductId_CombinationSignature");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductVariants_TenantId_CompanyId",
                table: "AppSalesChannelTrN11ProductStockItems",
                newName: "IX_AppSalesChannelTrN11ProductStockItems_TenantId_CompanyId");

            migrationBuilder.RenameColumn(
                name: "OverrideHeaderId",
                table: "AppSalesChannelTrN11ProductStockItemRecipeLines",
                newName: "StockItemId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductVariantRecipeLines_TenantId_SalesChannelTrN11ProductId_OverrideHeaderId_LineOrder",
                table: "AppSalesChannelTrN11ProductStockItemRecipeLines",
                newName: "IX_AppSalesChannelTrN11ProductStockItemRecipeLines_TenantId_SalesChannelTrN11ProductId_StockItemId_LineOrder");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductVariantRecipeLines_TenantId_CompanyId",
                table: "AppSalesChannelTrN11ProductStockItemRecipeLines",
                newName: "IX_AppSalesChannelTrN11ProductStockItemRecipeLines_TenantId_CompanyId");

            migrationBuilder.RenameColumn(
                name: "AxisId",
                table: "AppSalesChannelTrN11ProductAttributeValues",
                newName: "AttributeId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductAttributeAxisValues_TenantId_CompanyId",
                table: "AppSalesChannelTrN11ProductAttributeValues",
                newName: "IX_AppSalesChannelTrN11ProductAttributeValues_TenantId_CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductAttributeAxisValues_TenantId_AxisId",
                table: "AppSalesChannelTrN11ProductAttributeValues",
                newName: "IX_AppSalesChannelTrN11ProductAttributeValues_TenantId_AttributeId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductAttributeAxes_TenantId_SalesChannelTrN11ProductId",
                table: "AppSalesChannelTrN11ProductAttributes",
                newName: "IX_AppSalesChannelTrN11ProductAttributes_TenantId_SalesChannelTrN11ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductAttributeAxes_TenantId_CompanyId",
                table: "AppSalesChannelTrN11ProductAttributes",
                newName: "IX_AppSalesChannelTrN11ProductAttributes_TenantId_CompanyId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductStockItems",
                table: "AppSalesChannelTrN11ProductStockItems",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductStockItemRecipeLines",
                table: "AppSalesChannelTrN11ProductStockItemRecipeLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductAttributeValues",
                table: "AppSalesChannelTrN11ProductAttributeValues",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductAttributes",
                table: "AppSalesChannelTrN11ProductAttributes",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductStockItems",
                table: "AppSalesChannelTrN11ProductStockItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductStockItemRecipeLines",
                table: "AppSalesChannelTrN11ProductStockItemRecipeLines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductAttributeValues",
                table: "AppSalesChannelTrN11ProductAttributeValues");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductAttributes",
                table: "AppSalesChannelTrN11ProductAttributes");

            migrationBuilder.RenameTable(
                name: "AppSalesChannelTrN11ProductStockItems",
                newName: "AppSalesChannelTrN11ProductVariants");

            migrationBuilder.RenameTable(
                name: "AppSalesChannelTrN11ProductStockItemRecipeLines",
                newName: "AppSalesChannelTrN11ProductVariantRecipeLines");

            migrationBuilder.RenameTable(
                name: "AppSalesChannelTrN11ProductAttributeValues",
                newName: "AppSalesChannelTrN11ProductAttributeAxisValues");

            migrationBuilder.RenameTable(
                name: "AppSalesChannelTrN11ProductAttributes",
                newName: "AppSalesChannelTrN11ProductAttributeAxes");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductStockItems_TenantId_SalesChannelTrN11ProductId_ProductVariantId",
                table: "AppSalesChannelTrN11ProductVariants",
                newName: "IX_AppSalesChannelTrN11ProductVariants_TenantId_SalesChannelTrN11ProductId_ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductStockItems_TenantId_SalesChannelTrN11ProductId_CombinationSignature",
                table: "AppSalesChannelTrN11ProductVariants",
                newName: "IX_AppSalesChannelTrN11ProductVariants_TenantId_SalesChannelTrN11ProductId_CombinationSignature");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductStockItems_TenantId_CompanyId",
                table: "AppSalesChannelTrN11ProductVariants",
                newName: "IX_AppSalesChannelTrN11ProductVariants_TenantId_CompanyId");

            migrationBuilder.RenameColumn(
                name: "StockItemId",
                table: "AppSalesChannelTrN11ProductVariantRecipeLines",
                newName: "OverrideHeaderId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductStockItemRecipeLines_TenantId_SalesChannelTrN11ProductId_StockItemId_LineOrder",
                table: "AppSalesChannelTrN11ProductVariantRecipeLines",
                newName: "IX_AppSalesChannelTrN11ProductVariantRecipeLines_TenantId_SalesChannelTrN11ProductId_OverrideHeaderId_LineOrder");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductStockItemRecipeLines_TenantId_CompanyId",
                table: "AppSalesChannelTrN11ProductVariantRecipeLines",
                newName: "IX_AppSalesChannelTrN11ProductVariantRecipeLines_TenantId_CompanyId");

            migrationBuilder.RenameColumn(
                name: "AttributeId",
                table: "AppSalesChannelTrN11ProductAttributeAxisValues",
                newName: "AxisId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductAttributeValues_TenantId_CompanyId",
                table: "AppSalesChannelTrN11ProductAttributeAxisValues",
                newName: "IX_AppSalesChannelTrN11ProductAttributeAxisValues_TenantId_CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductAttributeValues_TenantId_AttributeId",
                table: "AppSalesChannelTrN11ProductAttributeAxisValues",
                newName: "IX_AppSalesChannelTrN11ProductAttributeAxisValues_TenantId_AxisId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductAttributes_TenantId_SalesChannelTrN11ProductId",
                table: "AppSalesChannelTrN11ProductAttributeAxes",
                newName: "IX_AppSalesChannelTrN11ProductAttributeAxes_TenantId_SalesChannelTrN11ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_AppSalesChannelTrN11ProductAttributes_TenantId_CompanyId",
                table: "AppSalesChannelTrN11ProductAttributeAxes",
                newName: "IX_AppSalesChannelTrN11ProductAttributeAxes_TenantId_CompanyId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductVariants",
                table: "AppSalesChannelTrN11ProductVariants",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductVariantRecipeLines",
                table: "AppSalesChannelTrN11ProductVariantRecipeLines",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductAttributeAxisValues",
                table: "AppSalesChannelTrN11ProductAttributeAxisValues",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppSalesChannelTrN11ProductAttributeAxes",
                table: "AppSalesChannelTrN11ProductAttributeAxes",
                column: "Id");
        }
    }
}
