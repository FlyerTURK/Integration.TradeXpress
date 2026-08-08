using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelPushGuardFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MaxPrice",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinPrice",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SafetyStock",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxPrice",
                table: "AppSalesChannelTrN11Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinPrice",
                table: "AppSalesChannelTrN11Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SafetyStock",
                table: "AppSalesChannelTrN11Products",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxPrice",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "MinPrice",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "SafetyStock",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "MaxPrice",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.DropColumn(
                name: "MinPrice",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.DropColumn(
                name: "SafetyStock",
                table: "AppSalesChannelTrN11Products");
        }
    }
}
