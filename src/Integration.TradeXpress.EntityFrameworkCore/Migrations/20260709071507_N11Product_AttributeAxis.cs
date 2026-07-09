using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class N11Product_AttributeAxis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannelTrN11ProductVariants_TenantId_SalesChannelTrN11ProductId_ProductVariantId",
                table: "AppSalesChannelTrN11ProductVariants");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductVariantId",
                table: "AppSalesChannelTrN11ProductVariants",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateTable(
                name: "AppSalesChannelTrN11ProductAttributeAxes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelTrN11ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_AppSalesChannelTrN11ProductAttributeAxes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSalesChannelTrN11ProductAttributeAxisValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AxisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_AppSalesChannelTrN11ProductAttributeAxisValues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11ProductVariants_TenantId_SalesChannelTrN11ProductId_ProductVariantId",
                table: "AppSalesChannelTrN11ProductVariants",
                columns: new[] { "TenantId", "SalesChannelTrN11ProductId", "ProductVariantId" },
                unique: true,
                filter: "[ProductVariantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11ProductAttributeAxes_TenantId_CompanyId",
                table: "AppSalesChannelTrN11ProductAttributeAxes",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11ProductAttributeAxes_TenantId_SalesChannelTrN11ProductId",
                table: "AppSalesChannelTrN11ProductAttributeAxes",
                columns: new[] { "TenantId", "SalesChannelTrN11ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11ProductAttributeAxisValues_TenantId_AxisId",
                table: "AppSalesChannelTrN11ProductAttributeAxisValues",
                columns: new[] { "TenantId", "AxisId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11ProductAttributeAxisValues_TenantId_CompanyId",
                table: "AppSalesChannelTrN11ProductAttributeAxisValues",
                columns: new[] { "TenantId", "CompanyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSalesChannelTrN11ProductAttributeAxes");

            migrationBuilder.DropTable(
                name: "AppSalesChannelTrN11ProductAttributeAxisValues");

            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannelTrN11ProductVariants_TenantId_SalesChannelTrN11ProductId_ProductVariantId",
                table: "AppSalesChannelTrN11ProductVariants");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductVariantId",
                table: "AppSalesChannelTrN11ProductVariants",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11ProductVariants_TenantId_SalesChannelTrN11ProductId_ProductVariantId",
                table: "AppSalesChannelTrN11ProductVariants",
                columns: new[] { "TenantId", "SalesChannelTrN11ProductId", "ProductVariantId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }
    }
}
