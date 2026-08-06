using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class ProductSaleStatusAndVariantVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "SaleStatus",
                table: "AppProductVariantDetails",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "AppProductVariantDetails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerifiedBy",
                table: "AppProductVariantDetails",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedRecipeStamp",
                table: "AppProductVariantDetails",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "SaleStatus",
                table: "AppProducts",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SaleStatus",
                table: "AppProductVariantDetails");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "AppProductVariantDetails");

            migrationBuilder.DropColumn(
                name: "VerifiedBy",
                table: "AppProductVariantDetails");

            migrationBuilder.DropColumn(
                name: "VerifiedRecipeStamp",
                table: "AppProductVariantDetails");

            migrationBuilder.DropColumn(
                name: "SaleStatus",
                table: "AppProducts");
        }
    }
}
