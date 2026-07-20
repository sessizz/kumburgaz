using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDevirOpeningBalanceAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CollectionAllocations_DuesInstallments_DuesInstallmentId",
                table: "CollectionAllocations");

            migrationBuilder.AlterColumn<int>(
                name: "DuesInstallmentId",
                table: "CollectionAllocations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "CollectionAllocations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionAllocations_UnitId",
                table: "CollectionAllocations",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_CollectionAllocations_DuesInstallments_DuesInstallmentId",
                table: "CollectionAllocations",
                column: "DuesInstallmentId",
                principalTable: "DuesInstallments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CollectionAllocations_Units_UnitId",
                table: "CollectionAllocations",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CollectionAllocations_Units_UnitId",
                table: "CollectionAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_CollectionAllocations_DuesInstallments_DuesInstallmentId",
                table: "CollectionAllocations");

            migrationBuilder.DropIndex(
                name: "IX_CollectionAllocations_UnitId",
                table: "CollectionAllocations");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "CollectionAllocations");

            migrationBuilder.AlterColumn<int>(
                name: "DuesInstallmentId",
                table: "CollectionAllocations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int?),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CollectionAllocations_DuesInstallments_DuesInstallmentId",
                table: "CollectionAllocations",
                column: "DuesInstallmentId",
                principalTable: "DuesInstallments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
