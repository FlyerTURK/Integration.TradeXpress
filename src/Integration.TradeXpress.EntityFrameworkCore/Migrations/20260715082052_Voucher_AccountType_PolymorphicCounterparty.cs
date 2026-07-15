using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Voucher_AccountType_PolymorphicCounterparty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppVouchers_AppAccounts_AccountId",
                table: "AppVouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_AppVouchers_AppSubAccounts_SubAccountId",
                table: "AppVouchers");

            migrationBuilder.DropIndex(
                name: "IX_AppVouchers_AccountId",
                table: "AppVouchers");

            migrationBuilder.DropIndex(
                name: "IX_AppVouchers_SubAccountId",
                table: "AppVouchers");

            migrationBuilder.AlterColumn<Guid>(
                name: "SubAccountId",
                table: "AppVouchers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountCode",
                table: "AppVouchers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte>(
                name: "AccountType",
                table: "AppVouchers",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "SubAccountCode",
                table: "AppVouchers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<Guid>(
                name: "SubAccountId",
                table: "AppBalanceLedgerEntries",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountCode",
                table: "AppBalanceLedgerEntries",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte>(
                name: "AccountType",
                table: "AppBalanceLedgerEntries",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "SubAccountCode",
                table: "AppBalanceLedgerEntries",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountCode",
                table: "AppVouchers");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "AppVouchers");

            migrationBuilder.DropColumn(
                name: "SubAccountCode",
                table: "AppVouchers");

            migrationBuilder.DropColumn(
                name: "AccountCode",
                table: "AppBalanceLedgerEntries");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "AppBalanceLedgerEntries");

            migrationBuilder.DropColumn(
                name: "SubAccountCode",
                table: "AppBalanceLedgerEntries");

            migrationBuilder.AlterColumn<Guid>(
                name: "SubAccountId",
                table: "AppVouchers",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "SubAccountId",
                table: "AppBalanceLedgerEntries",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_AppVouchers_AccountId",
                table: "AppVouchers",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AppVouchers_SubAccountId",
                table: "AppVouchers",
                column: "SubAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppVouchers_AppAccounts_AccountId",
                table: "AppVouchers",
                column: "AccountId",
                principalTable: "AppAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppVouchers_AppSubAccounts_SubAccountId",
                table: "AppVouchers",
                column: "SubAccountId",
                principalTable: "AppSubAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
