using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Trendyol_StockItemAndAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannelTrTrendyolProductVariants_TenantId_SalesChannelTrTrendyolProductId_ProductVariantId",
                table: "AppSalesChannelTrTrendyolProductVariants");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductVariantId",
                table: "AppSalesChannelTrTrendyolProductVariants",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "CombinationSignature",
                table: "AppSalesChannelTrTrendyolProductVariants",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppSalesChannelTrTrendyolProductAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelTrTrendyolProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_AppSalesChannelTrTrendyolProductAttributes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSalesChannelTrTrendyolProductAttributeValues",
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
                    table.PrimaryKey("PK_AppSalesChannelTrTrendyolProductAttributeValues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrTrendyolProductVariants_TenantId_SalesChannelTrTrendyolProductId_CombinationSignature",
                table: "AppSalesChannelTrTrendyolProductVariants",
                columns: new[] { "TenantId", "SalesChannelTrTrendyolProductId", "CombinationSignature" },
                unique: true,
                filter: "[CombinationSignature] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrTrendyolProductVariants_TenantId_SalesChannelTrTrendyolProductId_ProductVariantId",
                table: "AppSalesChannelTrTrendyolProductVariants",
                columns: new[] { "TenantId", "SalesChannelTrTrendyolProductId", "ProductVariantId" },
                unique: true,
                filter: "[ProductVariantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrTrendyolProductAttributes_TenantId_CompanyId",
                table: "AppSalesChannelTrTrendyolProductAttributes",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrTrendyolProductAttributes_TenantId_SalesChannelTrTrendyolProductId",
                table: "AppSalesChannelTrTrendyolProductAttributes",
                columns: new[] { "TenantId", "SalesChannelTrTrendyolProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrTrendyolProductAttributeValues_TenantId_AttributeId",
                table: "AppSalesChannelTrTrendyolProductAttributeValues",
                columns: new[] { "TenantId", "AttributeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrTrendyolProductAttributeValues_TenantId_CompanyId",
                table: "AppSalesChannelTrTrendyolProductAttributeValues",
                columns: new[] { "TenantId", "CompanyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSalesChannelTrTrendyolProductAttributes");

            migrationBuilder.DropTable(
                name: "AppSalesChannelTrTrendyolProductAttributeValues");

            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannelTrTrendyolProductVariants_TenantId_SalesChannelTrTrendyolProductId_CombinationSignature",
                table: "AppSalesChannelTrTrendyolProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannelTrTrendyolProductVariants_TenantId_SalesChannelTrTrendyolProductId_ProductVariantId",
                table: "AppSalesChannelTrTrendyolProductVariants");

            migrationBuilder.DropColumn(
                name: "CombinationSignature",
                table: "AppSalesChannelTrTrendyolProductVariants");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductVariantId",
                table: "AppSalesChannelTrTrendyolProductVariants",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrTrendyolProductVariants_TenantId_SalesChannelTrTrendyolProductId_ProductVariantId",
                table: "AppSalesChannelTrTrendyolProductVariants",
                columns: new[] { "TenantId", "SalesChannelTrTrendyolProductId", "ProductVariantId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }
    }
}
