using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Good_PricingToVariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppGoods_AppCurrencyUnits_EntryPriceUnitId",
                table: "AppGoods");

            migrationBuilder.DropForeignKey(
                name: "FK_AppGoods_AppCurrencyUnits_ExitPriceUnitId",
                table: "AppGoods");

            migrationBuilder.DropIndex(
                name: "IX_AppGoods_EntryPriceUnitId",
                table: "AppGoods");

            migrationBuilder.DropIndex(
                name: "IX_AppGoods_ExitPriceUnitId",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "EntryPrice",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "EntryPriceTaxIncluded",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "EntryPriceUnitId",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "ExitPrice",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "ExitPriceTaxIncluded",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "ExitPriceUnitId",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "Margin_Type",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "Margin_Value",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "MaxQuantity",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "MinQuantity",
                table: "AppGoods");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EntryPrice",
                table: "AppGoods",
                type: "decimal(18,5)",
                precision: 18,
                scale: 5,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "EntryPriceTaxIncluded",
                table: "AppGoods",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "EntryPriceUnitId",
                table: "AppGoods",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExitPrice",
                table: "AppGoods",
                type: "decimal(18,5)",
                precision: 18,
                scale: 5,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ExitPriceTaxIncluded",
                table: "AppGoods",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ExitPriceUnitId",
                table: "AppGoods",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Margin_Type",
                table: "AppGoods",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Margin_Value",
                table: "AppGoods",
                type: "decimal(18,5)",
                precision: 18,
                scale: 5,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxQuantity",
                table: "AppGoods",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinQuantity",
                table: "AppGoods",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppGoods_EntryPriceUnitId",
                table: "AppGoods",
                column: "EntryPriceUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AppGoods_ExitPriceUnitId",
                table: "AppGoods",
                column: "ExitPriceUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppGoods_AppCurrencyUnits_EntryPriceUnitId",
                table: "AppGoods",
                column: "EntryPriceUnitId",
                principalTable: "AppCurrencyUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppGoods_AppCurrencyUnits_ExitPriceUnitId",
                table: "AppGoods",
                column: "ExitPriceUnitId",
                principalTable: "AppCurrencyUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
