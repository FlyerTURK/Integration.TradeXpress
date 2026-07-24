using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AlterSubstitutionItemVariantsDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "IncludedVariantIds",
                table: "AppSubstitutionGroupItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldDefaultValueSql: "N'[]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "IncludedVariantIds",
                table: "AppSubstitutionGroupItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'[]'",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldDefaultValueSql: "'[]'");
        }
    }
}
