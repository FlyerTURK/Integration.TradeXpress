using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_ProductRecipeLine_PayLeg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PayFactor",
                table: "AppProductVariantRecipeLines",
                type: "decimal(18,5)",
                precision: 18,
                scale: 5,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PayUnitId",
                table: "AppProductVariantRecipeLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "PaymentType",
                table: "AppProductVariantRecipeLines",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayFactor",
                table: "AppProductVariantRecipeLines");

            migrationBuilder.DropColumn(
                name: "PayUnitId",
                table: "AppProductVariantRecipeLines");

            migrationBuilder.DropColumn(
                name: "PaymentType",
                table: "AppProductVariantRecipeLines");
        }
    }
}
