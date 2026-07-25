using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class CommodityPerCompanyOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppStones_TenantId_CompanyId_Code",
                table: "AppStones");

            migrationBuilder.DropIndex(
                name: "IX_AppMetals_TenantId_CompanyId_Code",
                table: "AppMetals");

            migrationBuilder.DropIndex(
                name: "IX_AppJewelries_TenantId_CompanyId_Code",
                table: "AppJewelries");

            migrationBuilder.DropIndex(
                name: "IX_AppGoods_TenantId_CompanyId_Code",
                table: "AppGoods");

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "AppStones",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "AppMetals",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "AppJewelries",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "AppGoods",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppStones_TenantId_CompanyId_Code",
                table: "AppStones",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppMetals_TenantId_CompanyId_Code",
                table: "AppMetals",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppJewelries_TenantId_CompanyId_Code",
                table: "AppJewelries",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppGoods_TenantId_CompanyId_Code",
                table: "AppGoods",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppStones_TenantId_CompanyId_Code",
                table: "AppStones");

            migrationBuilder.DropIndex(
                name: "IX_AppMetals_TenantId_CompanyId_Code",
                table: "AppMetals");

            migrationBuilder.DropIndex(
                name: "IX_AppJewelries_TenantId_CompanyId_Code",
                table: "AppJewelries");

            migrationBuilder.DropIndex(
                name: "IX_AppGoods_TenantId_CompanyId_Code",
                table: "AppGoods");

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "AppStones",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "AppMetals",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "AppJewelries",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                table: "AppGoods",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_AppStones_TenantId_CompanyId_Code",
                table: "AppStones",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppMetals_TenantId_CompanyId_Code",
                table: "AppMetals",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppJewelries_TenantId_CompanyId_Code",
                table: "AppJewelries",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppGoods_TenantId_CompanyId_Code",
                table: "AppGoods",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [CompanyId] IS NOT NULL");
        }
    }
}
