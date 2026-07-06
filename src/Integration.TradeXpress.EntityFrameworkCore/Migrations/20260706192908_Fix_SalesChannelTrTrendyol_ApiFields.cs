using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Fix_SalesChannelTrTrendyol_ApiFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AppSecret",
                table: "AppSalesChannelTrTrendyol",
                newName: "SellerId");

            migrationBuilder.RenameColumn(
                name: "AppKey",
                table: "AppSalesChannelTrTrendyol",
                newName: "ApiSecret");

            migrationBuilder.AddColumn<string>(
                name: "ApiKey",
                table: "AppSalesChannelTrTrendyol",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiKey",
                table: "AppSalesChannelTrTrendyol");

            migrationBuilder.RenameColumn(
                name: "SellerId",
                table: "AppSalesChannelTrTrendyol",
                newName: "AppSecret");

            migrationBuilder.RenameColumn(
                name: "ApiSecret",
                table: "AppSalesChannelTrTrendyol",
                newName: "AppKey");
        }
    }
}
