using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class ExpandAddressUbl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DispatchAddress_AdditionalStreetName",
                table: "AppShipmentTemplates",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchAddress_BuildingName",
                table: "AppShipmentTemplates",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchAddress_BuildingNumber",
                table: "AppShipmentTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchAddress_Floor",
                table: "AppShipmentTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchAddress_Postbox",
                table: "AppShipmentTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchAddress_Room",
                table: "AppShipmentTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnAddress_AdditionalStreetName",
                table: "AppShipmentTemplates",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnAddress_BuildingName",
                table: "AppShipmentTemplates",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnAddress_BuildingNumber",
                table: "AppShipmentTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnAddress_Floor",
                table: "AppShipmentTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnAddress_Postbox",
                table: "AppShipmentTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnAddress_Room",
                table: "AppShipmentTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeAddress_AdditionalStreetName",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeAddress_BuildingName",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeAddress_BuildingNumber",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeAddress_Floor",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeAddress_Postbox",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeAddress_Room",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseAddress_AdditionalStreetName",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseAddress_BuildingName",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseAddress_BuildingNumber",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseAddress_Floor",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseAddress_Postbox",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseAddress_Room",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_AdditionalStreetName",
                table: "AppBranches",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_BuildingName",
                table: "AppBranches",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_BuildingNumber",
                table: "AppBranches",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Floor",
                table: "AppBranches",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Postbox",
                table: "AppBranches",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Room",
                table: "AppBranches",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DispatchAddress_AdditionalStreetName",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "DispatchAddress_BuildingName",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "DispatchAddress_BuildingNumber",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "DispatchAddress_Floor",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "DispatchAddress_Postbox",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "DispatchAddress_Room",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ReturnAddress_AdditionalStreetName",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ReturnAddress_BuildingName",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ReturnAddress_BuildingNumber",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ReturnAddress_Floor",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ReturnAddress_Postbox",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ReturnAddress_Room",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ExchangeAddress_AdditionalStreetName",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ExchangeAddress_BuildingName",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ExchangeAddress_BuildingNumber",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ExchangeAddress_Floor",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ExchangeAddress_Postbox",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ExchangeAddress_Room",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "WarehouseAddress_AdditionalStreetName",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "WarehouseAddress_BuildingName",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "WarehouseAddress_BuildingNumber",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "WarehouseAddress_Floor",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "WarehouseAddress_Postbox",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "WarehouseAddress_Room",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "Address_AdditionalStreetName",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_BuildingName",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_BuildingNumber",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_Floor",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_Postbox",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_Room",
                table: "AppBranches");
        }
    }
}
