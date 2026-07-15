using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_MediaFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "AppMedia",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppMediaFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_AppMediaFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceVaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestinationVaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SourceVoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DestinationVoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppMedia_FolderId",
                table: "AppMedia",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_AppMediaFolders_TenantId_CompanyId_ParentId",
                table: "AppMediaFolders",
                columns: new[] { "TenantId", "CompanyId", "ParentId" });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppMediaFolders");

            migrationBuilder.DropTable(
                name: "AppTransfers");

            migrationBuilder.DropIndex(
                name: "IX_AppMedia_FolderId",
                table: "AppMedia");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "AppMedia");
        }
    }
}
