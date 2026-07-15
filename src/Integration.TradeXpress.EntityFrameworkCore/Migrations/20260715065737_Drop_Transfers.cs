using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Drop_Transfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppTransfers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrencyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecisionNote = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DestinationVaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationVoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Initiation = table.Column<byte>(type: "tinyint", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SourceVaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceVoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TransitInVoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TransitOutVoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TransitVaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppTransfers_AppCompanies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "AppCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTransfers_AppCurrencyUnits_CurrencyUnitId",
                        column: x => x.CurrencyUnitId,
                        principalTable: "AppCurrencyUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTransfers_AppVaults_DestinationVaultId",
                        column: x => x.DestinationVaultId,
                        principalTable: "AppVaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTransfers_AppVaults_SourceVaultId",
                        column: x => x.SourceVaultId,
                        principalTable: "AppVaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppTransfers_AppVaults_TransitVaultId",
                        column: x => x.TransitVaultId,
                        principalTable: "AppVaults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppTransfers_CompanyId",
                table: "AppTransfers",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTransfers_CurrencyUnitId",
                table: "AppTransfers",
                column: "CurrencyUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTransfers_DestinationVaultId",
                table: "AppTransfers",
                column: "DestinationVaultId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTransfers_SourceVaultId",
                table: "AppTransfers",
                column: "SourceVaultId");

            migrationBuilder.CreateIndex(
                name: "IX_AppTransfers_TenantId_CompanyId_DestinationVaultId_Status",
                table: "AppTransfers",
                columns: new[] { "TenantId", "CompanyId", "DestinationVaultId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTransfers_TenantId_CompanyId_SourceVaultId_Status",
                table: "AppTransfers",
                columns: new[] { "TenantId", "CompanyId", "SourceVaultId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTransfers_TransitVaultId",
                table: "AppTransfers",
                column: "TransitVaultId");
        }
    }
}
