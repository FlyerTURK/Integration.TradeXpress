using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_VoucherLine_Bullion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AssayAmount",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssayOfficeId",
                table: "AppVoucherLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "BullionType",
                table: "AppVoucherLines",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GoldLaborUnitId",
                table: "AppVoucherLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GoldLaborUnitRate",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GoldRate",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsExtra",
                table: "AppVoucherLines",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReport",
                table: "AppVoucherLines",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "LaborMode",
                table: "AppVoucherLines",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PalladiumFactor",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PalladiumLaborRate",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PalladiumLaborUnitId",
                table: "AppVoucherLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PalladiumLaborUnitRate",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "PalladiumMode",
                table: "AppVoucherLines",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PalladiumRate",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PalladiumUnitId",
                table: "AppVoucherLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatinumFactor",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatinumLaborRate",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlatinumLaborUnitId",
                table: "AppVoucherLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatinumLaborUnitRate",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "PlatinumMode",
                table: "AppVoucherLines",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatinumRate",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlatinumUnitId",
                table: "AppVoucherLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportNo",
                table: "AppVoucherLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SilverFactor",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SilverLaborRate",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SilverLaborUnitId",
                table: "AppVoucherLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SilverLaborUnitRate",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "SilverMode",
                table: "AppVoucherLines",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SilverRate",
                table: "AppVoucherLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SilverUnitId",
                table: "AppVoucherLines",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssayAmount",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "AssayOfficeId",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "BullionType",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "GoldLaborUnitId",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "GoldLaborUnitRate",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "GoldRate",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "IsExtra",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "IsReport",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "LaborMode",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PalladiumFactor",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PalladiumLaborRate",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PalladiumLaborUnitId",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PalladiumLaborUnitRate",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PalladiumMode",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PalladiumRate",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PalladiumUnitId",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PlatinumFactor",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PlatinumLaborRate",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PlatinumLaborUnitId",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PlatinumLaborUnitRate",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PlatinumMode",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PlatinumRate",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "PlatinumUnitId",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "ReportNo",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "SilverFactor",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "SilverLaborRate",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "SilverLaborUnitId",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "SilverLaborUnitRate",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "SilverMode",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "SilverRate",
                table: "AppVoucherLines");

            migrationBuilder.DropColumn(
                name: "SilverUnitId",
                table: "AppVoucherLines");
        }
    }
}
