using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddN11ChannelDefaultInfos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultExchangeInfo",
                table: "AppSalesChannelTrN11",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultInstallmentInfo",
                table: "AppSalesChannelTrN11",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultShippingInfo",
                table: "AppSalesChannelTrN11",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultExchangeInfo",
                table: "AppSalesChannelTrN11");

            migrationBuilder.DropColumn(
                name: "DefaultInstallmentInfo",
                table: "AppSalesChannelTrN11");

            migrationBuilder.DropColumn(
                name: "DefaultShippingInfo",
                table: "AppSalesChannelTrN11");
        }
    }
}
