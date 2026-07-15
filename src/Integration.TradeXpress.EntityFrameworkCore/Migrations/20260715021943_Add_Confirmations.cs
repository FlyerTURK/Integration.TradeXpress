using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_Confirmations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppConfirmations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InitiatorVaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CounterpartyVaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessType = table.Column<byte>(type: "tinyint", nullable: false),
                    Direction = table.Column<byte>(type: "tinyint", nullable: false),
                    CurrencyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    InitiatorVoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CounterpartyVoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConfirmations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppConfirmations_AppCompanies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "AppCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppConfirmations_AppCurrencyUnits_CurrencyUnitId",
                        column: x => x.CurrencyUnitId,
                        principalTable: "AppCurrencyUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppConfirmations_AppVaults_CounterpartyVaultId",
                        column: x => x.CounterpartyVaultId,
                        principalTable: "AppVaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppConfirmations_AppVaults_InitiatorVaultId",
                        column: x => x.InitiatorVaultId,
                        principalTable: "AppVaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppConfirmations_CompanyId",
                table: "AppConfirmations",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_AppConfirmations_CounterpartyVaultId",
                table: "AppConfirmations",
                column: "CounterpartyVaultId");

            migrationBuilder.CreateIndex(
                name: "IX_AppConfirmations_CurrencyUnitId",
                table: "AppConfirmations",
                column: "CurrencyUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AppConfirmations_InitiatorVaultId",
                table: "AppConfirmations",
                column: "InitiatorVaultId");

            migrationBuilder.CreateIndex(
                name: "IX_AppConfirmations_TenantId_CompanyId_CounterpartyVaultId_Status",
                table: "AppConfirmations",
                columns: new[] { "TenantId", "CompanyId", "CounterpartyVaultId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppConfirmations_TenantId_CompanyId_InitiatorVaultId_Status",
                table: "AppConfirmations",
                columns: new[] { "TenantId", "CompanyId", "InitiatorVaultId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppConfirmations");
        }
    }
}
