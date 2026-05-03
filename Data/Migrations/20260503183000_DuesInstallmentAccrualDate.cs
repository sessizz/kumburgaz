using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DuesInstallmentAccrualDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AccrualDate",
                table: "DuesInstallments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "DuesInstallments"
                SET "AccrualDate" = "DueDate"
                WHERE "AccrualDate" IS NULL;
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "AccrualDate",
                table: "DuesInstallments",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccrualDate",
                table: "DuesInstallments");
        }
    }
}
