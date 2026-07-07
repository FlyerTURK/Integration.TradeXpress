using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_N11ChannelProduct_SellerCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SellerCode",
                table: "AppSalesChannelTrN11Products",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SequenceNo",
                table: "AppSalesChannelTrN11Products",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SellerCode",
                table: "AppSalesChannelTrN11Products");

            migrationBuilder.DropColumn(
                name: "SequenceNo",
                table: "AppSalesChannelTrN11Products");
        }
    }
}
