using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class StockPolicyBackfillAndSingleMainIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AppEntityVariants_SingleMain",
                table: "AppEntityVariants",
                columns: new[] { "TenantId", "EntityName", "EntityId" },
                unique: true,
                filter: "[IsMain] = 1 AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppEntityVariants_SingleMain",
                table: "AppEntityVariants");
        }
    }
}
