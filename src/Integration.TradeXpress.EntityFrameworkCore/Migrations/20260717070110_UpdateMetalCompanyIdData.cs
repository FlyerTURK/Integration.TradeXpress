using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMetalCompanyIdData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE AppMetals SET CompanyId = (SELECT TOP 1 CompanyId FROM AppEntityVariants WHERE AppEntityVariants.EntityId = AppMetals.Id) WHERE CompanyId IS NULL AND TenantId IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
