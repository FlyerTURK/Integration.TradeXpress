using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceProductDomesticWithOriginCountry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Domestic",
                table: "AppProducts");

            migrationBuilder.AddColumn<Guid>(
                name: "OriginCountryId",
                table: "AppProducts",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginCountryId",
                table: "AppProducts");

            migrationBuilder.AddColumn<bool>(
                name: "Domestic",
                table: "AppProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
