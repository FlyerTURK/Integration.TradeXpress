using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class GoodVariantDetail_PricingStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppGoodVariantDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntityVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockUnitCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    IsQuantity = table.Column<bool>(type: "bit", nullable: false),
                    MinQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    MaxQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    EntryPrice = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    EntryPriceUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntryPriceTaxIncluded = table.Column<bool>(type: "bit", nullable: false),
                    ExitPrice = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    ExitPriceUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExitPriceTaxIncluded = table.Column<bool>(type: "bit", nullable: false),
                    Margin_Type = table.Column<int>(type: "int", nullable: false),
                    Margin_Value = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
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
                    table.PrimaryKey("PK_AppGoodVariantDetails", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppGoodVariantDetails_TenantId_CompanyId",
                table: "AppGoodVariantDetails",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppGoodVariantDetails_TenantId_EntityVariantId",
                table: "AppGoodVariantDetails",
                columns: new[] { "TenantId", "EntityVariantId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppGoodVariantDetails");
        }
    }
}
