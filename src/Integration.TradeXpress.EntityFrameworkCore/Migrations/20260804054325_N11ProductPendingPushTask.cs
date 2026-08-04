using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class N11ProductPendingPushTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PendingPushTaskAt",
                table: "AppSalesChannelTrN11Products",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingPushTaskId",
                table: "AppSalesChannelTrN11Products",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingPushTaskAt",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.DropColumn(
                name: "PendingPushTaskId",
                table: "AppSalesChannelTrN11Products");
        }
    }
}
