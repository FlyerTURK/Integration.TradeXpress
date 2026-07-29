using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class ShipmentTariffRatesAsOwnedTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AppMarketplaceShipmentTariffRates",
                table: "AppMarketplaceShipmentTariffRates");

            migrationBuilder.DropIndex(
                name: "IX_AppMarketplaceShipmentTariffRates_TariffId_Desi",
                table: "AppMarketplaceShipmentTariffRates");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "AppMarketplaceShipmentTariffRates");

            migrationBuilder.DropColumn(
                name: "CreationTime",
                table: "AppMarketplaceShipmentTariffRates");

            migrationBuilder.DropColumn(
                name: "CreatorId",
                table: "AppMarketplaceShipmentTariffRates");

            migrationBuilder.DropColumn(
                name: "DeleterId",
                table: "AppMarketplaceShipmentTariffRates");

            migrationBuilder.DropColumn(
                name: "DeletionTime",
                table: "AppMarketplaceShipmentTariffRates");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "AppMarketplaceShipmentTariffRates");

            migrationBuilder.DropColumn(
                name: "LastModificationTime",
                table: "AppMarketplaceShipmentTariffRates");

            migrationBuilder.DropColumn(
                name: "LastModifierId",
                table: "AppMarketplaceShipmentTariffRates");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppMarketplaceShipmentTariffRates",
                table: "AppMarketplaceShipmentTariffRates",
                columns: new[] { "TariffId", "Desi" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AppMarketplaceShipmentTariffRates",
                table: "AppMarketplaceShipmentTariffRates");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "AppMarketplaceShipmentTariffRates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreationTime",
                table: "AppMarketplaceShipmentTariffRates",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatorId",
                table: "AppMarketplaceShipmentTariffRates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeleterId",
                table: "AppMarketplaceShipmentTariffRates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionTime",
                table: "AppMarketplaceShipmentTariffRates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "AppMarketplaceShipmentTariffRates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModificationTime",
                table: "AppMarketplaceShipmentTariffRates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastModifierId",
                table: "AppMarketplaceShipmentTariffRates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppMarketplaceShipmentTariffRates",
                table: "AppMarketplaceShipmentTariffRates",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_AppMarketplaceShipmentTariffRates_TariffId_Desi",
                table: "AppMarketplaceShipmentTariffRates",
                columns: new[] { "TariffId", "Desi" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
