using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class RecipeMetalReverseIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AppProductVariantRecipeLines_TenantId_CompanyId_CommodityProcessType_CommodityId_CommodityVariantId",
                table: "AppProductVariantRecipeLines",
                columns: new[] { "TenantId", "CompanyId", "CommodityProcessType", "CommodityId", "CommodityVariantId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppProductVariantRecipeLines_TenantId_CompanyId_CommodityProcessType_CommodityId_CommodityVariantId",
                table: "AppProductVariantRecipeLines");
        }
    }
}
