using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_N11Category_Commission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionRate",
                table: "AppN11Categories",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketingFeeRate",
                table: "AppN11Categories",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketplaceFeeRate",
                table: "AppN11Categories",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayoutDays",
                table: "AppN11Categories",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommissionRate",
                table: "AppN11Categories");

            migrationBuilder.DropColumn(
                name: "MarketingFeeRate",
                table: "AppN11Categories");

            migrationBuilder.DropColumn(
                name: "MarketplaceFeeRate",
                table: "AppN11Categories");

            migrationBuilder.DropColumn(
                name: "PayoutDays",
                table: "AppN11Categories");
        }
    }
}
