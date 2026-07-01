using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeLedgerTransfersCategoryless : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LedgerTransactions_IncomeExpenseCategories_IncomeExpenseCat~",
                table: "LedgerTransactions");

            migrationBuilder.AlterColumn<int>(
                name: "IncomeExpenseCategoryId",
                table: "LedgerTransactions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<bool>(
                name: "IsTransfer",
                table: "LedgerTransactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TransferIsIncoming",
                table: "LedgerTransactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "LedgerTransactions"
                SET "IsTransfer" = TRUE,
                    "TransferIsIncoming" = CASE
                        WHEN "IncomeExpenseCategoryId" IN (
                            SELECT "Id"
                            FROM "IncomeExpenseCategories"
                            WHERE "Type" = 'Gelir'
                        )
                        THEN TRUE
                        ELSE FALSE
                    END,
                    "IncomeExpenseCategoryId" = NULL
                WHERE "IncomeExpenseCategoryId" IN (
                        SELECT "Id"
                        FROM "IncomeExpenseCategories"
                        WHERE "Name" = 'Para Transferi'
                           OR "Name" = 'Transfer'
                    )
                   OR "Description" LIKE 'Para transferi:%'
                   OR "Description" LIKE 'Bankaya yatır:%'
                   OR "Description" LIKE 'Bankaya yatir:%'
                   OR "Description" = 'Para transferi'
                   OR "Description" = 'Bankaya yatır'
                   OR "Description" = 'Bankaya yatir';
                """);

            migrationBuilder.Sql("""
                DELETE FROM "IncomeExpenseCategories"
                WHERE "Name" = 'Para Transferi'
                  AND "Type" IN ('Gelir', 'Gider');
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerTransactions_IncomeExpenseCategories_IncomeExpenseCat~",
                table: "LedgerTransactions",
                column: "IncomeExpenseCategoryId",
                principalTable: "IncomeExpenseCategories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LedgerTransactions_IncomeExpenseCategories_IncomeExpenseCat~",
                table: "LedgerTransactions");

            migrationBuilder.Sql("""
                INSERT INTO "IncomeExpenseCategories" ("Name", "Type", "Active")
                SELECT 'Para Transferi', 'Gelir', TRUE
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "IncomeExpenseCategories"
                    WHERE "Name" = 'Para Transferi'
                      AND "Type" = 'Gelir'
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO "IncomeExpenseCategories" ("Name", "Type", "Active")
                SELECT 'Para Transferi', 'Gider', TRUE
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "IncomeExpenseCategories"
                    WHERE "Name" = 'Para Transferi'
                      AND "Type" = 'Gider'
                );
                """);

            migrationBuilder.Sql("""
                UPDATE "LedgerTransactions"
                SET "IncomeExpenseCategoryId" = (
                    SELECT "Id"
                    FROM "IncomeExpenseCategories"
                    WHERE "Name" = 'Para Transferi'
                      AND "Type" = CASE
                          WHEN "LedgerTransactions"."TransferIsIncoming" = TRUE THEN 'Gelir'
                          ELSE 'Gider'
                      END
                    LIMIT 1
                )
                WHERE "IsTransfer" = TRUE
                  AND "IncomeExpenseCategoryId" IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE "LedgerTransactions"
                SET "IncomeExpenseCategoryId" = (
                    SELECT "Id"
                    FROM "IncomeExpenseCategories"
                    WHERE "Type" = 'Gider'
                    ORDER BY "Id"
                    LIMIT 1
                )
                WHERE "IncomeExpenseCategoryId" IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "IsTransfer",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "TransferIsIncoming",
                table: "LedgerTransactions");

            migrationBuilder.AlterColumn<int>(
                name: "IncomeExpenseCategoryId",
                table: "LedgerTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerTransactions_IncomeExpenseCategories_IncomeExpenseCat~",
                table: "LedgerTransactions",
                column: "IncomeExpenseCategoryId",
                principalTable: "IncomeExpenseCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
