using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class SideCost_Settings_And_RecipeKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InsuredShippingEnabled",
                table: "AppSalesChannelTrTrendyolProductStockItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "SideCostKind",
                table: "AppSalesChannelTrTrendyolProductStockItemRecipeLines",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InsuredShippingEnabled",
                table: "AppSalesChannelTrN11ProductStockItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "SideCostKind",
                table: "AppSalesChannelTrN11ProductStockItemRecipeLines",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SideCosts",
                table: "AppSalesChannels",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "SideCostKind",
                table: "AppProductVariantRecipeLines",
                type: "tinyint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InsuredShippingEnabled",
                table: "AppSalesChannelTrTrendyolProductStockItems");

            migrationBuilder.DropColumn(
                name: "SideCostKind",
                table: "AppSalesChannelTrTrendyolProductStockItemRecipeLines");

            migrationBuilder.DropColumn(
                name: "InsuredShippingEnabled",
                table: "AppSalesChannelTrN11ProductStockItems");

            migrationBuilder.DropColumn(
                name: "SideCostKind",
                table: "AppSalesChannelTrN11ProductStockItemRecipeLines");

            migrationBuilder.DropColumn(
                name: "SideCosts",
                table: "AppSalesChannels");

            migrationBuilder.DropColumn(
                name: "SideCostKind",
                table: "AppProductVariantRecipeLines");
        }
    }
}
