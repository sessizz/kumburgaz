using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitPhoneAndMoveInDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MoveInDate",
                table: "Units",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Units",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Units",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "MoveInDate", "Phone" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Units",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "MoveInDate", "Phone" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Units",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "MoveInDate", "Phone" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MoveInDate",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Units");
        }
    }
}
