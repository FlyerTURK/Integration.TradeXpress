using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Product_MarketplaceDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Condition",
                table: "AppProducts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyUnitId",
                table: "AppProducts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Domestic",
                table: "AppProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxPurchaseQuantity",
                table: "AppProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreparingDay",
                table: "AppProducts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SellerNote",
                table: "AppProducts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipmentTemplateName",
                table: "AppProducts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecialInfo",
                table: "AppProducts",
                type: "nvarchar(max)",
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Condition",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "CurrencyUnitId",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "Domestic",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "MaxPurchaseQuantity",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "PreparingDay",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "SellerNote",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "ShipmentTemplateName",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "SpecialInfo",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "UnitType",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "UnitWeight",
                table: "AppProducts");
        }
    }
}
