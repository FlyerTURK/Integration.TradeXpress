using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class ProductCoreCategoryLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProductCategoryId",
                table: "AppProducts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppProducts_TenantId_CompanyId_ProductCategoryId",
                table: "AppProducts",
                columns: new[] { "TenantId", "CompanyId", "ProductCategoryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppProducts_TenantId_CompanyId_ProductCategoryId",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "ProductCategoryId",
                table: "AppProducts");
        }
    }
}
