using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class CurrencyUnitMargin_AppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppCurrencyUnitMargins_TenantId_CurrencyUnitId",
                table: "AppCurrencyUnitMargins");

            migrationBuilder.DropColumn(
                name: "DeleterId",
                table: "AppCurrencyUnitMargins");

            migrationBuilder.DropColumn(
                name: "DeletionTime",
                table: "AppCurrencyUnitMargins");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "AppCurrencyUnitMargins");

            migrationBuilder.DropColumn(
                name: "LastModificationTime",
                table: "AppCurrencyUnitMargins");

            migrationBuilder.DropColumn(
                name: "LastModifierId",
                table: "AppCurrencyUnitMargins");

            migrationBuilder.CreateIndex(
                name: "IX_AppCurrencyUnitMargins_TenantId_CurrencyUnitId_CreationTime",
                table: "AppCurrencyUnitMargins",
                columns: new[] { "TenantId", "CurrencyUnitId", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppCurrencyUnitMargins_TenantId_CurrencyUnitId_CreationTime",
                table: "AppCurrencyUnitMargins");

            migrationBuilder.AddColumn<Guid>(
                name: "DeleterId",
                table: "AppCurrencyUnitMargins",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionTime",
                table: "AppCurrencyUnitMargins",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "AppCurrencyUnitMargins",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModificationTime",
                table: "AppCurrencyUnitMargins",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastModifierId",
                table: "AppCurrencyUnitMargins",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCurrencyUnitMargins_TenantId_CurrencyUnitId",
                table: "AppCurrencyUnitMargins",
                columns: new[] { "TenantId", "CurrencyUnitId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }
    }
}
