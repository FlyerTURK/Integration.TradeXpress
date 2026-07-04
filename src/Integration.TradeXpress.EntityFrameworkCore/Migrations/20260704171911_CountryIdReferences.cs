using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class CountryIdReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppCompanies_TenantId_CountryCode",
                table: "AppCompanies");

            migrationBuilder.AlterColumn<string>(
                name: "DefaultCurrencyCode",
                table: "AppCountries",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultCurrencyUnitId",
                table: "AppCountries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CountryCode",
                table: "AppCompanies",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2)",
                oldMaxLength: 2);

            migrationBuilder.AddColumn<Guid>(
                name: "CountryId",
                table: "AppCompanies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCountries_DefaultCurrencyUnitId",
                table: "AppCountries",
                column: "DefaultCurrencyUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCompanies_TenantId_CountryId",
                table: "AppCompanies",
                columns: new[] { "TenantId", "CountryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppCountries_DefaultCurrencyUnitId",
                table: "AppCountries");

            migrationBuilder.DropIndex(
                name: "IX_AppCompanies_TenantId_CountryId",
                table: "AppCompanies");

            migrationBuilder.DropColumn(
                name: "DefaultCurrencyUnitId",
                table: "AppCountries");

            migrationBuilder.DropColumn(
                name: "CountryId",
                table: "AppCompanies");

            migrationBuilder.AlterColumn<string>(
                name: "DefaultCurrencyCode",
                table: "AppCountries",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CountryCode",
                table: "AppCompanies",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(2)",
                oldMaxLength: 2,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCompanies_TenantId_CountryCode",
                table: "AppCompanies",
                columns: new[] { "TenantId", "CountryCode" });
        }
    }
}
