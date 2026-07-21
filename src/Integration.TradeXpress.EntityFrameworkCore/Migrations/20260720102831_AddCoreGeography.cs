using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddCoreGeography : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CoreLocalityId",
                table: "AppN11Districts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CoreAdministrativeAreaId",
                table: "AppN11Cities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Alpha3Code",
                table: "AppCountries",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NumericCode",
                table: "AppCountries",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UsesAdministrativeArea",
                table: "AppCountries",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "UsesSubLocality",
                table: "AppCountries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AppAdministrativeAreas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Iso3166_2Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_AppAdministrativeAreas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppLocalities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AdministrativeAreaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK_AppLocalities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSubLocalities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocalityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
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
                    table.PrimaryKey("PK_AppSubLocalities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppN11Districts_CoreLocalityId",
                table: "AppN11Districts",
                column: "CoreLocalityId");

            migrationBuilder.CreateIndex(
                name: "IX_AppN11Cities_CoreAdministrativeAreaId",
                table: "AppN11Cities",
                column: "CoreAdministrativeAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_AppAdministrativeAreas_CountryId",
                table: "AppAdministrativeAreas",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_AppAdministrativeAreas_CountryId_Iso3166_2Code",
                table: "AppAdministrativeAreas",
                columns: new[] { "CountryId", "Iso3166_2Code" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Iso3166_2Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppLocalities_AdministrativeAreaId",
                table: "AppLocalities",
                column: "AdministrativeAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLocalities_CountryId",
                table: "AppLocalities",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSubLocalities_LocalityId",
                table: "AppSubLocalities",
                column: "LocalityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAdministrativeAreas");

            migrationBuilder.DropTable(
                name: "AppLocalities");

            migrationBuilder.DropTable(
                name: "AppSubLocalities");

            migrationBuilder.DropIndex(
                name: "IX_AppN11Districts_CoreLocalityId",
                table: "AppN11Districts");

            migrationBuilder.DropIndex(
                name: "IX_AppN11Cities_CoreAdministrativeAreaId",
                table: "AppN11Cities");

            migrationBuilder.DropColumn(
                name: "CoreLocalityId",
                table: "AppN11Districts");

            migrationBuilder.DropColumn(
                name: "CoreAdministrativeAreaId",
                table: "AppN11Cities");

            migrationBuilder.DropColumn(
                name: "Alpha3Code",
                table: "AppCountries");

            migrationBuilder.DropColumn(
                name: "NumericCode",
                table: "AppCountries");

            migrationBuilder.DropColumn(
                name: "UsesAdministrativeArea",
                table: "AppCountries");

            migrationBuilder.DropColumn(
                name: "UsesSubLocality",
                table: "AppCountries");
        }
    }
}
