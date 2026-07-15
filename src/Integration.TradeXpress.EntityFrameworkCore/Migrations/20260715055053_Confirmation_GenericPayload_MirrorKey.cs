using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Confirmation_GenericPayload_MirrorKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppConfirmations_AppCurrencyUnits_CurrencyUnitId",
                table: "AppConfirmations");

            migrationBuilder.DropIndex(
                name: "IX_AppConfirmations_CurrencyUnitId",
                table: "AppConfirmations");

            migrationBuilder.DropColumn(
                name: "CurrencyUnitId",
                table: "AppConfirmations");

            migrationBuilder.RenameColumn(
                name: "PayloadJson",
                table: "AppConfirmations",
                newName: "CounterpartyPayloadJson");

            migrationBuilder.AddColumn<Guid>(
                name: "CommodityId",
                table: "AppConfirmations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InitiatorPayloadJson",
                table: "AppConfirmations",
                type: "nvarchar(max)",
                maxLength: 8192,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "MainUnitId",
                table: "AppConfirmations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PayTotal",
                table: "AppConfirmations",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PayUnitId",
                table: "AppConfirmations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "AppConfirmations",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "AppConfirmations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppConfirmations_MainUnitId",
                table: "AppConfirmations",
                column: "MainUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AppConfirmations_PayUnitId",
                table: "AppConfirmations",
                column: "PayUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppConfirmations_AppCurrencyUnits_MainUnitId",
                table: "AppConfirmations",
                column: "MainUnitId",
                principalTable: "AppCurrencyUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppConfirmations_AppCurrencyUnits_PayUnitId",
                table: "AppConfirmations",
                column: "PayUnitId",
                principalTable: "AppCurrencyUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppConfirmations_AppCurrencyUnits_MainUnitId",
                table: "AppConfirmations");

            migrationBuilder.DropForeignKey(
                name: "FK_AppConfirmations_AppCurrencyUnits_PayUnitId",
                table: "AppConfirmations");

            migrationBuilder.DropIndex(
                name: "IX_AppConfirmations_MainUnitId",
                table: "AppConfirmations");

            migrationBuilder.DropIndex(
                name: "IX_AppConfirmations_PayUnitId",
                table: "AppConfirmations");

            migrationBuilder.DropColumn(
                name: "CommodityId",
                table: "AppConfirmations");

            migrationBuilder.DropColumn(
                name: "InitiatorPayloadJson",
                table: "AppConfirmations");

            migrationBuilder.DropColumn(
                name: "MainUnitId",
                table: "AppConfirmations");

            migrationBuilder.DropColumn(
                name: "PayTotal",
                table: "AppConfirmations");

            migrationBuilder.DropColumn(
                name: "PayUnitId",
                table: "AppConfirmations");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "AppConfirmations");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "AppConfirmations");

            migrationBuilder.RenameColumn(
                name: "CounterpartyPayloadJson",
                table: "AppConfirmations",
                newName: "PayloadJson");

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyUnitId",
                table: "AppConfirmations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_AppConfirmations_CurrencyUnitId",
                table: "AppConfirmations",
                column: "CurrencyUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppConfirmations_AppCurrencyUnits_CurrencyUnitId",
                table: "AppConfirmations",
                column: "CurrencyUnitId",
                principalTable: "AppCurrencyUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
