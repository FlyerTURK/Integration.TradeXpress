using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelRecipeCommodityVariantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CommodityVariantId",
                table: "AppSalesChannelTrTrendyolProductStockItemRecipeLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CommodityVariantId",
                table: "AppSalesChannelTrN11ProductStockItemRecipeLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CommodityVariantId",
                table: "AppSalesChannelEtsyProductStockItemRecipeLines",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommodityVariantId",
                table: "AppSalesChannelTrTrendyolProductStockItemRecipeLines");

            migrationBuilder.DropColumn(
                name: "CommodityVariantId",
                table: "AppSalesChannelTrN11ProductStockItemRecipeLines");

            migrationBuilder.DropColumn(
                name: "CommodityVariantId",
                table: "AppSalesChannelEtsyProductStockItemRecipeLines");
        }
    }
}
