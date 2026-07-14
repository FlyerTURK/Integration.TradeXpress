using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Good_RetailCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EntryPriceTaxIncluded",
                table: "AppGoods",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExitPriceTaxIncluded",
                table: "AppGoods",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "MainSupplierAccountId",
                table: "AppGoods",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MainSupplierSubAccountId",
                table: "AppGoods",
                type: "uniqueidentifier",
                nullable: true);

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
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinQuantity",
                table: "AppGoods",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OtvRate",
                table: "AppGoods",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "StockUnitCode",
                table: "AppGoods",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplyDays",
                table: "AppGoods",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatPurchaseRate",
                table: "AppGoods",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatSaleRate",
                table: "AppGoods",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WithholdingRate",
                table: "AppGoods",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "AppGoodSuppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GoodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    CurrencyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaxIncluded = table.Column<bool>(type: "bit", nullable: false),
                    LeadDays = table.Column<int>(type: "int", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppGoodSuppliers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppGoodSuppliers_GoodId",
                table: "AppGoodSuppliers",
                column: "GoodId");

            migrationBuilder.CreateIndex(
                name: "IX_AppGoodSuppliers_SubAccountId",
                table: "AppGoodSuppliers",
                column: "SubAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppGoodSuppliers");

            migrationBuilder.DropColumn(
                name: "EntryPriceTaxIncluded",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "ExitPriceTaxIncluded",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "MainSupplierAccountId",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "MainSupplierSubAccountId",
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

            migrationBuilder.DropColumn(
                name: "OtvRate",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "StockUnitCode",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "SupplyDays",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "VatPurchaseRate",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "VatSaleRate",
                table: "AppGoods");

            migrationBuilder.DropColumn(
                name: "WithholdingRate",
                table: "AppGoods");
        }
    }
}
