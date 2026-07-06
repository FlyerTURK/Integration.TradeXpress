using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_ProductRecipeLine_Derived : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "DerivedBaseMode",
                table: "AppProductVariantRecipeLines",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DerivedOperand",
                table: "AppProductVariantRecipeLines",
                type: "decimal(18,5)",
                precision: 18,
                scale: 5,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<byte>(
                name: "DerivedOperation",
                table: "AppProductVariantRecipeLines",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DerivedSourceLineIds",
                table: "AppProductVariantRecipeLines",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DerivedBaseMode",
                table: "AppProductVariantRecipeLines");

            migrationBuilder.DropColumn(
                name: "DerivedOperand",
                table: "AppProductVariantRecipeLines");

            migrationBuilder.DropColumn(
                name: "DerivedOperation",
                table: "AppProductVariantRecipeLines");

            migrationBuilder.DropColumn(
                name: "DerivedSourceLineIds",
                table: "AppProductVariantRecipeLines");
        }
    }
}
