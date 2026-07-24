using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Address_AdministrativeAreaId",
                table: "AppBranches",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_AdministrativeAreaIsoCode",
                table: "AppBranches",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_City",
                table: "AppBranches",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_CityCode",
                table: "AppBranches",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_CountryCode",
                table: "AppBranches",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_District",
                table: "AppBranches",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_DistrictCode",
                table: "AppBranches",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Line",
                table: "AppBranches",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Address_LocalityId",
                table: "AppBranches",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Neighborhood",
                table: "AppBranches",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_PostalCode",
                table: "AppBranches",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Title",
                table: "AppBranches",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address_AdministrativeAreaId",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_AdministrativeAreaIsoCode",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_City",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_CityCode",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_CountryCode",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_District",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_DistrictCode",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_Line",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_LocalityId",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_Neighborhood",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_PostalCode",
                table: "AppBranches");

            migrationBuilder.DropColumn(
                name: "Address_Title",
                table: "AppBranches");
        }
    }
}
