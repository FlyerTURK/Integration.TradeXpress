using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class ProductCategoryDropCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppProductCategories_TenantId_CompanyId_Code",
                table: "AppProductCategories");

            migrationBuilder.DropIndex(
                name: "IX_AppProductCategories_TenantId_CompanyId_ParentId",
                table: "AppProductCategories");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "AppProductCategories");

            migrationBuilder.CreateIndex(
                name: "IX_AppProductCategories_TenantId_CompanyId_ParentId_Name",
                table: "AppProductCategories",
                columns: new[] { "TenantId", "CompanyId", "ParentId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppProductCategories_TenantId_CompanyId_ParentId_Name",
                table: "AppProductCategories");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "AppProductCategories",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_AppProductCategories_TenantId_CompanyId_Code",
                table: "AppProductCategories",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppProductCategories_TenantId_CompanyId_ParentId",
                table: "AppProductCategories",
                columns: new[] { "TenantId", "CompanyId", "ParentId" });
        }
    }
}
