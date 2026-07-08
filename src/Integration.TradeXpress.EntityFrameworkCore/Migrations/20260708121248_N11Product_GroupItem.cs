using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class N11Product_GroupItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroupAttribute",
                table: "AppSalesChannelTrN11Products",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupItemCode",
                table: "AppSalesChannelTrN11Products",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "AppSalesChannelTrN11Products",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupAttribute",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.DropColumn(
                name: "GroupItemCode",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "AppSalesChannelTrN11Products");
        }
    }
}
