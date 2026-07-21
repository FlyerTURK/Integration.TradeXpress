using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddAddressGeographyRefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OriginAddress_AdministrativeAreaId",
                table: "AppShipmentTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginAddress_AdministrativeAreaIsoCode",
                table: "AppShipmentTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginAddress_LocalityId",
                table: "AppShipmentTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnAddress_AdministrativeAreaId",
                table: "AppShipmentTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnAddress_AdministrativeAreaIsoCode",
                table: "AppShipmentTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnAddress_LocalityId",
                table: "AppShipmentTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeAddress_AdministrativeAreaId",
                table: "AppN11ShipmentTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeAddress_AdministrativeAreaIsoCode",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeAddress_LocalityId",
                table: "AppN11ShipmentTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseAddress_AdministrativeAreaId",
                table: "AppN11ShipmentTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseAddress_AdministrativeAreaIsoCode",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseAddress_LocalityId",
                table: "AppN11ShipmentTemplates",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginAddress_AdministrativeAreaId",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "OriginAddress_AdministrativeAreaIsoCode",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "OriginAddress_LocalityId",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ReturnAddress_AdministrativeAreaId",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ReturnAddress_AdministrativeAreaIsoCode",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ReturnAddress_LocalityId",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ExchangeAddress_AdministrativeAreaId",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ExchangeAddress_AdministrativeAreaIsoCode",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ExchangeAddress_LocalityId",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "WarehouseAddress_AdministrativeAreaId",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "WarehouseAddress_AdministrativeAreaIsoCode",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "WarehouseAddress_LocalityId",
                table: "AppN11ShipmentTemplates");
        }
    }
}
