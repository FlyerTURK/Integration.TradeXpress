using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Trendyol_ProductSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ListPrice",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RemoteApproved",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RemoteOnSale",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteProductMainId",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppProductVariants_TenantId_Barcode",
                table: "AppProductVariants",
                columns: new[] { "TenantId", "Barcode" },
                unique: true,
                filter: "[Barcode] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppProductVariants_TenantId_Barcode",
                table: "AppProductVariants");

            migrationBuilder.DropColumn(
                name: "ListPrice",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "RemoteApproved",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "RemoteOnSale",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "RemoteProductMainId",
                table: "AppSalesChannelTrTrendyolProducts");
        }
    }
}
