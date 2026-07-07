using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class ChannelProduct_AllowMultiplePerChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannelTrTrendyolProducts_SalesChannelId_ProductId",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannelTrN11Products_SalesChannelId_ProductId",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrTrendyolProducts_SalesChannelId_ProductId",
                table: "AppSalesChannelTrTrendyolProducts",
                columns: new[] { "SalesChannelId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11Products_SalesChannelId_ProductId",
                table: "AppSalesChannelTrN11Products",
                columns: new[] { "SalesChannelId", "ProductId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannelTrTrendyolProducts_SalesChannelId_ProductId",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannelTrN11Products_SalesChannelId_ProductId",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrTrendyolProducts_SalesChannelId_ProductId",
                table: "AppSalesChannelTrTrendyolProducts",
                columns: new[] { "SalesChannelId", "ProductId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11Products_SalesChannelId_ProductId",
                table: "AppSalesChannelTrN11Products",
                columns: new[] { "SalesChannelId", "ProductId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
