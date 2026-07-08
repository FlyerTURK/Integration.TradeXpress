using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class N11ChannelProduct_VariantRecipe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSalesChannelTrN11ProductVariantRecipeLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelTrN11ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_AppSalesChannelTrN11ProductVariantRecipeLines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11ProductVariantRecipeLines_TenantId_CompanyId",
                table: "AppSalesChannelTrN11ProductVariantRecipeLines",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11ProductVariantRecipeLines_TenantId_SalesChannelTrN11ProductId_ProductVariantId_LineOrder",
                table: "AppSalesChannelTrN11ProductVariantRecipeLines",
                columns: new[] { "TenantId", "SalesChannelTrN11ProductId", "ProductVariantId", "LineOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSalesChannelTrN11ProductVariantRecipeLines");
        }
    }
}
