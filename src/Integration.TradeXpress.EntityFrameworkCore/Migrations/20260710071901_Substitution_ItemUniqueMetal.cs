using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Substitution_ItemUniqueMetal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AppSubstitutionGroupItems_TenantId_SubstitutionGroupId_MetalId",
                table: "AppSubstitutionGroupItems",
                columns: new[] { "TenantId", "SubstitutionGroupId", "MetalId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [MetalId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppSubstitutionGroupItems_TenantId_SubstitutionGroupId_MetalId",
                table: "AppSubstitutionGroupItems");
        }
    }
}
