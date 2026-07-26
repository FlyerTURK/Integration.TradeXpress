using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class TrCarrierSubAccountLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubAccountId",
                table: "AppTrCarriers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppTrCarriers_TenantId_SubAccountId",
                table: "AppTrCarriers",
                columns: new[] { "TenantId", "SubAccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppTrCarriers_TenantId_SubAccountId",
                table: "AppTrCarriers");

            migrationBuilder.DropColumn(
                name: "SubAccountId",
                table: "AppTrCarriers");
        }
    }
}
