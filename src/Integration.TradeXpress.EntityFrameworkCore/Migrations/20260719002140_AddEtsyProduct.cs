using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddEtsyProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSupply",
                table: "AppProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WhenMade",
                table: "AppProducts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WhoMade",
                table: "AppProducts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AppSalesChannelEtsyProductAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelEtsyProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_AppSalesChannelEtsyProductAttributes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSalesChannelEtsyProductAttributeValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttributeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_AppSalesChannelEtsyProductAttributeValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSalesChannelEtsyProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerSkuBase = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    TaxonomyId = table.Column<long>(type: "bigint", nullable: true),
                    ListingType = table.Column<int>(type: "int", nullable: false),
                    ShippingProfileId = table.Column<long>(type: "bigint", nullable: true),
                    ReturnPolicyId = table.Column<long>(type: "bigint", nullable: true),
                    ShopSectionId = table.Column<long>(type: "bigint", nullable: true),
                    ProcessingMin = table.Column<int>(type: "int", nullable: true),
                    ProcessingMax = table.Column<int>(type: "int", nullable: true),
                    TitleOverride = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    DescriptionOverride = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: true),
                    IsPersonalizable = table.Column<bool>(type: "bit", nullable: false),
                    PersonalizationInstructions = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PreparingDay = table.Column<int>(type: "int", nullable: false),
                    CurrencyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SellerNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EtsyListingId = table.Column<long>(type: "bigint", nullable: true),
                    ListingState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
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
                    Materials = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Skus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpecialInfo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSalesChannelEtsyProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSalesChannelEtsyProductStockItemRecipeLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelEtsyProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineOrder = table.Column<int>(type: "int", nullable: false),
                    ComponentType = table.Column<byte>(type: "tinyint", nullable: false),
                    CommodityProcessType = table.Column<byte>(type: "tinyint", nullable: true),
                    CommodityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    ValuationUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentType = table.Column<byte>(type: "tinyint", nullable: false),
                    PayFactor = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    PayUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ManualAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ManualUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DerivedBaseMode = table.Column<byte>(type: "tinyint", nullable: true),
                    DerivedOperation = table.Column<byte>(type: "tinyint", nullable: true),
                    DerivedOperand = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    DerivedSourceLineIds = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SideCostKind = table.Column<byte>(type: "tinyint", nullable: true),
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
                    table.PrimaryKey("PK_AppSalesChannelEtsyProductStockItemRecipeLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSalesChannelEtsyProductStockItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelEtsyProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverridePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OverridePriceCurrencyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverrideStock = table.Column<int>(type: "int", nullable: true),
                    Margin = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: true),
                    InsuredShippingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CombinationSignature = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
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
                    table.PrimaryKey("PK_AppSalesChannelEtsyProductStockItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelEtsyProductAttributes_TenantId_CompanyId",
                table: "AppSalesChannelEtsyProductAttributes",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelEtsyProductAttributes_TenantId_SalesChannelEtsyProductId",
                table: "AppSalesChannelEtsyProductAttributes",
                columns: new[] { "TenantId", "SalesChannelEtsyProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelEtsyProductAttributeValues_TenantId_AttributeId",
                table: "AppSalesChannelEtsyProductAttributeValues",
                columns: new[] { "TenantId", "AttributeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelEtsyProductAttributeValues_TenantId_CompanyId",
                table: "AppSalesChannelEtsyProductAttributeValues",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelEtsyProducts_SalesChannelId_ProductId",
                table: "AppSalesChannelEtsyProducts",
                columns: new[] { "SalesChannelId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelEtsyProducts_TenantId_CompanyId",
                table: "AppSalesChannelEtsyProducts",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelEtsyProductStockItemRecipeLines_TenantId_CompanyId",
                table: "AppSalesChannelEtsyProductStockItemRecipeLines",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelEtsyProductStockItemRecipeLines_TenantId_SalesChannelEtsyProductId_StockItemId_LineOrder",
                table: "AppSalesChannelEtsyProductStockItemRecipeLines",
                columns: new[] { "TenantId", "SalesChannelEtsyProductId", "StockItemId", "LineOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelEtsyProductStockItems_TenantId_CompanyId",
                table: "AppSalesChannelEtsyProductStockItems",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelEtsyProductStockItems_TenantId_SalesChannelEtsyProductId_CombinationSignature",
                table: "AppSalesChannelEtsyProductStockItems",
                columns: new[] { "TenantId", "SalesChannelEtsyProductId", "CombinationSignature" },
                unique: true,
                filter: "[CombinationSignature] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelEtsyProductStockItems_TenantId_SalesChannelEtsyProductId_ProductVariantId",
                table: "AppSalesChannelEtsyProductStockItems",
                columns: new[] { "TenantId", "SalesChannelEtsyProductId", "ProductVariantId" },
                unique: true,
                filter: "[ProductVariantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSalesChannelEtsyProductAttributes");

            migrationBuilder.DropTable(
                name: "AppSalesChannelEtsyProductAttributeValues");

            migrationBuilder.DropTable(
                name: "AppSalesChannelEtsyProducts");

            migrationBuilder.DropTable(
                name: "AppSalesChannelEtsyProductStockItemRecipeLines");

            migrationBuilder.DropTable(
                name: "AppSalesChannelEtsyProductStockItems");

            migrationBuilder.DropColumn(
                name: "IsSupply",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "WhenMade",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "WhoMade",
                table: "AppProducts");
        }
    }
}
