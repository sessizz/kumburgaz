using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CollectionUnitAndCategoryTurkish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "Collections",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "IncomeExpenseCategories",
                keyColumn: "Id",
                keyValue: 1,
                column: "Type",
                value: "Gelir");

            migrationBuilder.UpdateData(
                table: "IncomeExpenseCategories",
                keyColumn: "Id",
                keyValue: 2,
                column: "Type",
                value: "Gider");

            migrationBuilder.UpdateData(
                table: "IncomeExpenseCategories",
                keyColumn: "Id",
                keyValue: 3,
                column: "Type",
                value: "Gider");

            migrationBuilder.Sql("""
                UPDATE "IncomeExpenseCategories"
                SET "Type" = CASE
                    WHEN lower("Type") = 'income' THEN 'Gelir'
                    WHEN lower("Type") = 'expense' THEN 'Gider'
                    ELSE "Type"
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "Collections" c
                SET "UnitId" = bg_min.min_unit_id
                FROM (
                    SELECT "BillingGroupId", MIN("UnitId") AS min_unit_id
                    FROM "BillingGroupUnits"
                    GROUP BY "BillingGroupId"
                ) bg_min
                WHERE c."UnitId" IS NULL
                  AND c."BillingGroupId" = bg_min."BillingGroupId";
                """);

            migrationBuilder.Sql("""
                UPDATE "Collections"
                SET "UnitId" = (SELECT MIN("Id") FROM "Units")
                WHERE "UnitId" IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "UnitId",
                table: "Collections",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_UnitId",
                table: "Collections",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Collections_Units_UnitId",
                table: "Collections",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Collections_Units_UnitId",
                table: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Collections_UnitId",
                table: "Collections");

            migrationBuilder.AlterColumn<int>(
                name: "UnitId",
                table: "Collections",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "Collections");

            migrationBuilder.UpdateData(
                table: "IncomeExpenseCategories",
                keyColumn: "Id",
                keyValue: 1,
                column: "Type",
                value: "Income");

            migrationBuilder.UpdateData(
                table: "IncomeExpenseCategories",
                keyColumn: "Id",
                keyValue: 2,
                column: "Type",
                value: "Expense");

            migrationBuilder.UpdateData(
                table: "IncomeExpenseCategories",
                keyColumn: "Id",
                keyValue: 3,
                column: "Type",
                value: "Expense");
        }
    }
}
