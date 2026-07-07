using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Rename_ChannelProductTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppN11ProductListings");

            migrationBuilder.DropTable(
                name: "AppTrendyolProductListings");

            migrationBuilder.CreateTable(
                name: "AppSalesChannelTrN11Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryExternalId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CategoryName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Condition = table.Column<byte>(type: "tinyint", nullable: false),
                    ShipmentTemplateName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Domestic = table.Column<bool>(type: "bit", nullable: false),
                    PreparingDay = table.Column<int>(type: "int", nullable: false),
                    MaxPurchaseQuantity = table.Column<int>(type: "int", nullable: true),
                    N11ProductId = table.Column<long>(type: "bigint", nullable: true),
                    SaleStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ApprovalStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attributes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpecialInfo = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSalesChannelTrN11Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSalesChannelTrTrendyolProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CategoryName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    BrandId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    VatRate = table.Column<int>(type: "int", nullable: false),
                    CargoCompanyId = table.Column<int>(type: "int", nullable: true),
                    DimensionalWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    BatchRequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attributes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSalesChannelTrTrendyolProducts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11Products_SalesChannelId_ProductId",
                table: "AppSalesChannelTrN11Products",
                columns: new[] { "SalesChannelId", "ProductId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11Products_TenantId_CompanyId",
                table: "AppSalesChannelTrN11Products",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrTrendyolProducts_SalesChannelId_ProductId",
                table: "AppSalesChannelTrTrendyolProducts",
                columns: new[] { "SalesChannelId", "ProductId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrTrendyolProducts_TenantId_CompanyId",
                table: "AppSalesChannelTrTrendyolProducts",
                columns: new[] { "TenantId", "CompanyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSalesChannelTrN11Products");

            migrationBuilder.DropTable(
                name: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.CreateTable(
                name: "AppN11ProductListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CategoryExternalId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CategoryName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Condition = table.Column<byte>(type: "tinyint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Domestic = table.Column<bool>(type: "bit", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxPurchaseQuantity = table.Column<int>(type: "int", nullable: true),
                    N11ProductId = table.Column<long>(type: "bigint", nullable: true),
                    PreparingDay = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SalesChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentTemplateName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Attributes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpecialInfo = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppN11ProductListings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppTrendyolProductListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchRequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    BrandId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CargoCompanyId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CategoryName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DimensionalWeight = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastError = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VatRate = table.Column<int>(type: "int", nullable: false),
                    Attributes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppTrendyolProductListings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppN11ProductListings_SalesChannelId_ProductId",
                table: "AppN11ProductListings",
                columns: new[] { "SalesChannelId", "ProductId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppN11ProductListings_TenantId_CompanyId",
                table: "AppN11ProductListings",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTrendyolProductListings_SalesChannelId_ProductId",
                table: "AppTrendyolProductListings",
                columns: new[] { "SalesChannelId", "ProductId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppTrendyolProductListings_TenantId_CompanyId",
                table: "AppTrendyolProductListings",
                columns: new[] { "TenantId", "CompanyId" });
        }
    }
}
