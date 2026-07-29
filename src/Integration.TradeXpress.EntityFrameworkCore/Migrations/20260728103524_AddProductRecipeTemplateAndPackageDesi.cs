using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddProductRecipeTemplateAndPackageDesi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PackageDesi",
                table: "AppProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecipeTemplateId",
                table: "AppProducts",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackageDesi",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "RecipeTemplateId",
                table: "AppProducts");
        }
    }
}
