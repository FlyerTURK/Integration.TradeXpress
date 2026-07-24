using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddSubstitutionItemIncludedVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IncludedVariantIds",
                table: "AppSubstitutionGroupItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'[]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludedVariantIds",
                table: "AppSubstitutionGroupItems");
        }
    }
}
