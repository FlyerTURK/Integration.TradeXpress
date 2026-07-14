using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Order_LineActionStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActionAt",
                table: "AppOrderLineOperationalData",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ActionStatus",
                table: "AppOrderLineOperationalData",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "AppOrderLineOperationalData",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActionAt",
                table: "AppOrderLineOperationalData");

            migrationBuilder.DropColumn(
                name: "ActionStatus",
                table: "AppOrderLineOperationalData");

            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "AppOrderLineOperationalData");
        }
    }
}
