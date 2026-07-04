using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyIdToSubAccountAndVault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AppVaults",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AppSubAccounts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_AppVaults_TenantId_CompanyId",
                table: "AppVaults",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSubAccounts_TenantId_CompanyId",
                table: "AppSubAccounts",
                columns: new[] { "TenantId", "CompanyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppVaults_TenantId_CompanyId",
                table: "AppVaults");

            migrationBuilder.DropIndex(
                name: "IX_AppSubAccounts_TenantId_CompanyId",
                table: "AppSubAccounts");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AppVaults");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AppSubAccounts");
        }
    }
}
