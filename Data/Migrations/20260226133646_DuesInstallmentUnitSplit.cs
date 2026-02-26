using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DuesInstallmentUnitSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DuesInstallments_BillingGroupId_Period",
                table: "DuesInstallments");

            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "DuesInstallments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DuesInstallments_BillingGroupId_Period_UnitId",
                table: "DuesInstallments",
                columns: new[] { "BillingGroupId", "Period", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DuesInstallments_UnitId",
                table: "DuesInstallments",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_DuesInstallments_Units_UnitId",
                table: "DuesInstallments",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DuesInstallments_Units_UnitId",
                table: "DuesInstallments");

            migrationBuilder.DropIndex(
                name: "IX_DuesInstallments_BillingGroupId_Period_UnitId",
                table: "DuesInstallments");

            migrationBuilder.DropIndex(
                name: "IX_DuesInstallments_UnitId",
                table: "DuesInstallments");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "DuesInstallments");

            migrationBuilder.CreateIndex(
                name: "IX_DuesInstallments_BillingGroupId_Period",
                table: "DuesInstallments",
                columns: new[] { "BillingGroupId", "Period" },
                unique: true);
        }
    }
}
