using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_TransferTransit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Initiation",
                table: "AppTransfers",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<Guid>(
                name: "TransitInVoucherId",
                table: "AppTransfers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TransitOutVoucherId",
                table: "AppTransfers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TransitVaultId",
                table: "AppTransfers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppTransfers_TransitVaultId",
                table: "AppTransfers",
                column: "TransitVaultId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppTransfers_AppVaults_TransitVaultId",
                table: "AppTransfers",
                column: "TransitVaultId",
                principalTable: "AppVaults",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppTransfers_AppVaults_TransitVaultId",
                table: "AppTransfers");

            migrationBuilder.DropIndex(
                name: "IX_AppTransfers_TransitVaultId",
                table: "AppTransfers");

            migrationBuilder.DropColumn(
                name: "Initiation",
                table: "AppTransfers");

            migrationBuilder.DropColumn(
                name: "TransitInVoucherId",
                table: "AppTransfers");

            migrationBuilder.DropColumn(
                name: "TransitOutVoucherId",
                table: "AppTransfers");

            migrationBuilder.DropColumn(
                name: "TransitVaultId",
                table: "AppTransfers");
        }
    }
}
