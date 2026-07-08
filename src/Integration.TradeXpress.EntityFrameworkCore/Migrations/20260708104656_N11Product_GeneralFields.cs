using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class N11Product_GeneralFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyUnitId",
                table: "AppSalesChannelTrN11Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpirationDate",
                table: "AppSalesChannelTrN11Products",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProductionDate",
                table: "AppSalesChannelTrN11Products",
                type: "datetime2",
                nullable: true);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrencyUnitId",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.DropColumn(
                name: "ProductionDate",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.DropColumn(
                name: "UnitType",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.DropColumn(
                name: "UnitWeight",
                table: "AppSalesChannelTrN11Products");
        }
    }
}
