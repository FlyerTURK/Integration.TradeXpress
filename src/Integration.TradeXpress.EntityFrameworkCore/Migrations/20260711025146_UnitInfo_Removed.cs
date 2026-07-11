using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class UnitInfo_Removed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitType",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.DropColumn(
                name: "UnitWeight",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.DropColumn(
                name: "UnitType",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "UnitWeight",
                table: "AppProducts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UnitType",
                table: "AppSalesChannelTrN11Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitWeight",
                table: "AppSalesChannelTrN11Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitType",
                table: "AppProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitWeight",
                table: "AppProducts",
                type: "int",
                nullable: true);
        }
    }
}
