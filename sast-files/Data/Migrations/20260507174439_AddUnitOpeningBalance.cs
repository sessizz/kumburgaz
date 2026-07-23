using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitOpeningBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "Units",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.UpdateData(
                table: "Units",
                keyColumn: "Id",
                keyValue: 1,
                column: "OpeningBalance",
                value: 0m);

            migrationBuilder.UpdateData(
                table: "Units",
                keyColumn: "Id",
                keyValue: 2,
                column: "OpeningBalance",
                value: 0m);

            migrationBuilder.UpdateData(
                table: "Units",
                keyColumn: "Id",
                keyValue: 3,
                column: "OpeningBalance",
                value: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpeningBalance",
                table: "Units");
        }
    }
}
