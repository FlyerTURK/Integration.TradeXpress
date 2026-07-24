using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeShipmentDispatchReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OriginAddress_Title",
                table: "AppShipmentTemplates",
                newName: "DispatchAddress_Title");

            migrationBuilder.RenameColumn(
                name: "OriginAddress_PostalCode",
                table: "AppShipmentTemplates",
                newName: "DispatchAddress_PostalCode");

            migrationBuilder.RenameColumn(
                name: "OriginAddress_Neighborhood",
                table: "AppShipmentTemplates",
                newName: "DispatchAddress_Neighborhood");

            migrationBuilder.RenameColumn(
                name: "OriginAddress_LocalityId",
                table: "AppShipmentTemplates",
                newName: "DispatchAddress_LocalityId");

            migrationBuilder.RenameColumn(
                name: "OriginAddress_Line",
                table: "AppShipmentTemplates",
                newName: "DispatchAddress_Line");

            migrationBuilder.RenameColumn(
                name: "OriginAddress_DistrictCode",
                table: "AppShipmentTemplates",
                newName: "DispatchAddress_DistrictCode");

            migrationBuilder.RenameColumn(
                name: "OriginAddress_District",
                table: "AppShipmentTemplates",
                newName: "DispatchAddress_District");

            migrationBuilder.RenameColumn(
                name: "OriginAddress_CountryCode",
                table: "AppShipmentTemplates",
                newName: "DispatchAddress_CountryCode");

            migrationBuilder.RenameColumn(
                name: "OriginAddress_CityCode",
                table: "AppShipmentTemplates",
                newName: "DispatchAddress_CityCode");

            migrationBuilder.RenameColumn(
                name: "OriginAddress_City",
                table: "AppShipmentTemplates",
                newName: "DispatchAddress_City");

            migrationBuilder.RenameColumn(
                name: "OriginAddress_AdministrativeAreaIsoCode",
                table: "AppShipmentTemplates",
                newName: "DispatchAddress_AdministrativeAreaIsoCode");

            migrationBuilder.RenameColumn(
                name: "OriginAddress_AdministrativeAreaId",
                table: "AppShipmentTemplates",
                newName: "DispatchAddress_AdministrativeAreaId");

            migrationBuilder.AlterColumn<string>(
                name: "DispatchAddress_Line",
                table: "AppShipmentTemplates",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "DispatchAddress_CountryCode",
                table: "AppShipmentTemplates",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2)",
                oldMaxLength: 2);

            migrationBuilder.AlterColumn<string>(
                name: "DispatchAddress_City",
                table: "AppShipmentTemplates",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<Guid>(
                name: "DispatchBranchId",
                table: "AppShipmentTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnBranchId",
                table: "AppShipmentTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReturnSameAsDispatch",
                table: "AppShipmentTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DispatchBranchId",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ReturnBranchId",
                table: "AppShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ReturnSameAsDispatch",
                table: "AppShipmentTemplates");

            migrationBuilder.RenameColumn(
                name: "DispatchAddress_Title",
                table: "AppShipmentTemplates",
                newName: "OriginAddress_Title");

            migrationBuilder.RenameColumn(
                name: "DispatchAddress_PostalCode",
                table: "AppShipmentTemplates",
                newName: "OriginAddress_PostalCode");

            migrationBuilder.RenameColumn(
                name: "DispatchAddress_Neighborhood",
                table: "AppShipmentTemplates",
                newName: "OriginAddress_Neighborhood");

            migrationBuilder.RenameColumn(
                name: "DispatchAddress_LocalityId",
                table: "AppShipmentTemplates",
                newName: "OriginAddress_LocalityId");

            migrationBuilder.RenameColumn(
                name: "DispatchAddress_Line",
                table: "AppShipmentTemplates",
                newName: "OriginAddress_Line");

            migrationBuilder.RenameColumn(
                name: "DispatchAddress_DistrictCode",
                table: "AppShipmentTemplates",
                newName: "OriginAddress_DistrictCode");

            migrationBuilder.RenameColumn(
                name: "DispatchAddress_District",
                table: "AppShipmentTemplates",
                newName: "OriginAddress_District");

            migrationBuilder.RenameColumn(
                name: "DispatchAddress_CountryCode",
                table: "AppShipmentTemplates",
                newName: "OriginAddress_CountryCode");

            migrationBuilder.RenameColumn(
                name: "DispatchAddress_CityCode",
                table: "AppShipmentTemplates",
                newName: "OriginAddress_CityCode");

            migrationBuilder.RenameColumn(
                name: "DispatchAddress_City",
                table: "AppShipmentTemplates",
                newName: "OriginAddress_City");

            migrationBuilder.RenameColumn(
                name: "DispatchAddress_AdministrativeAreaIsoCode",
                table: "AppShipmentTemplates",
                newName: "OriginAddress_AdministrativeAreaIsoCode");

            migrationBuilder.RenameColumn(
                name: "DispatchAddress_AdministrativeAreaId",
                table: "AppShipmentTemplates",
                newName: "OriginAddress_AdministrativeAreaId");

            migrationBuilder.AlterColumn<string>(
                name: "OriginAddress_Line",
                table: "AppShipmentTemplates",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(512)",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OriginAddress_CountryCode",
                table: "AppShipmentTemplates",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(2)",
                oldMaxLength: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OriginAddress_City",
                table: "AppShipmentTemplates",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
