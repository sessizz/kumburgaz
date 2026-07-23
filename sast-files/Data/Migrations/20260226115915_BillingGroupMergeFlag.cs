using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class BillingGroupMergeFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMerged",
                table: "BillingGroups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "BillingGroups",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsMerged",
                value: true);

            migrationBuilder.UpdateData(
                table: "BillingGroups",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsMerged",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsMerged",
                table: "BillingGroups");
        }
    }
}
