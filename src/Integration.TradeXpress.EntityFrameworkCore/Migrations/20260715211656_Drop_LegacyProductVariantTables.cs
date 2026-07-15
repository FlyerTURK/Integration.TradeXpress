using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Drop_LegacyProductVariantTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppProductAttributes");

            migrationBuilder.DropTable(
                name: "AppProductAttributeValues");

            migrationBuilder.DropTable(
                name: "AppProductVariantAttributeValues");

            migrationBuilder.DropTable(
                name: "AppProductVariants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppProductAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppProductAttributes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppProductAttributeValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductAttributeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Value = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppProductAttributeValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppProductVariantAttributeValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductAttributeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductAttributeValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppProductVariantAttributeValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppProductVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Gtin = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsMain = table.Column<bool>(type: "bit", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Mpn = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Oem = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    SalePriceCurrencyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StockQuantity = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppProductVariants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppProductAttributes_TenantId_CompanyId",
                table: "AppProductAttributes",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppProductAttributes_TenantId_ProductId_Name",
                table: "AppProductAttributes",
                columns: new[] { "TenantId", "ProductId", "Name" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppProductAttributeValues_TenantId_CompanyId",
                table: "AppProductAttributeValues",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppProductAttributeValues_TenantId_ProductAttributeId_Value",
                table: "AppProductAttributeValues",
                columns: new[] { "TenantId", "ProductAttributeId", "Value" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppProductVariantAttributeValues_TenantId_CompanyId",
                table: "AppProductVariantAttributeValues",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppProductVariantAttributeValues_TenantId_ProductAttributeValueId",
                table: "AppProductVariantAttributeValues",
                columns: new[] { "TenantId", "ProductAttributeValueId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppProductVariantAttributeValues_TenantId_ProductVariantId_ProductAttributeId",
                table: "AppProductVariantAttributeValues",
                columns: new[] { "TenantId", "ProductVariantId", "ProductAttributeId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppProductVariants_TenantId_Barcode",
                table: "AppProductVariants",
                columns: new[] { "TenantId", "Barcode" },
                unique: true,
                filter: "[Barcode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppProductVariants_TenantId_CompanyId",
                table: "AppProductVariants",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppProductVariants_TenantId_ProductId_Code",
                table: "AppProductVariants",
                columns: new[] { "TenantId", "ProductId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppProductVariants_TenantId_ProductId_IsMain",
                table: "AppProductVariants",
                columns: new[] { "TenantId", "ProductId", "IsMain" });
        }
    }
}
