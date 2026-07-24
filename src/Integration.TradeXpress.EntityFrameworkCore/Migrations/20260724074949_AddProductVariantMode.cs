using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddProductVariantMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubstitutionGroupId",
                table: "AppProducts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubstitutionOverrideVariantIds",
                table: "AppProducts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<decimal>(
                name: "SubstitutionTargetQuantity",
                table: "AppProducts",
                type: "decimal(18,5)",
                precision: 18,
                scale: 5,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubstitutionToleranceType",
                table: "AppProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SubstitutionToleranceValue",
                table: "AppProducts",
                type: "decimal(18,5)",
                precision: 18,
                scale: 5,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VariantMode",
                table: "AppProducts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AppProducts_SubstitutionGroupId",
                table: "AppProducts",
                column: "SubstitutionGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppProducts_SubstitutionGroupId",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "SubstitutionGroupId",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "SubstitutionOverrideVariantIds",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "SubstitutionTargetQuantity",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "SubstitutionToleranceType",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "SubstitutionToleranceValue",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "VariantMode",
                table: "AppProducts");
        }
    }
}
