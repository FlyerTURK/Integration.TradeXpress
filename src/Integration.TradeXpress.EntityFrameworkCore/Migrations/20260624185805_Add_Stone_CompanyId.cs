using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_Stone_CompanyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppStones_TenantId_Code",
                table: "AppStones");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AppStones",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppStones_TenantId_CompanyId_Code",
                table: "AppStones",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [CompanyId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppStones_TenantId_CompanyId_Code",
                table: "AppStones");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AppStones");

            migrationBuilder.CreateIndex(
                name: "IX_AppStones_TenantId_Code",
                table: "AppStones",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }
    }
}
