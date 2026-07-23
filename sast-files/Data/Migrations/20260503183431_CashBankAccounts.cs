using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CashBankAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BankAccountId",
                table: "LedgerTransactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CashBoxId",
                table: "LedgerTransactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BankAccountId",
                table: "Collections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CashBoxId",
                table: "Collections",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Branch = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningBalanceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CashBoxes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningBalanceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashBoxes", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "CashBoxes",
                columns: new[] { "Id", "Active", "Name", "OpeningBalance", "OpeningBalanceDate" },
                values: new object[] { 1, true, "Kasa", 0m, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.Sql("""
                UPDATE "Collections"
                SET "CashBoxId" = 1
                WHERE "PaymentChannel" = 1 AND "CashBoxId" IS NULL AND "BankAccountId" IS NULL;

                UPDATE "LedgerTransactions"
                SET "CashBoxId" = 1
                WHERE "PaymentChannel" = 1 AND "CashBoxId" IS NULL AND "BankAccountId" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_BankAccountId",
                table: "LedgerTransactions",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_CashBoxId",
                table: "LedgerTransactions",
                column: "CashBoxId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_BankAccountId",
                table: "Collections",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_CashBoxId",
                table: "Collections",
                column: "CashBoxId");

            migrationBuilder.AddForeignKey(
                name: "FK_Collections_BankAccounts_BankAccountId",
                table: "Collections",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Collections_CashBoxes_CashBoxId",
                table: "Collections",
                column: "CashBoxId",
                principalTable: "CashBoxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerTransactions_BankAccounts_BankAccountId",
                table: "LedgerTransactions",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerTransactions_CashBoxes_CashBoxId",
                table: "LedgerTransactions",
                column: "CashBoxId",
                principalTable: "CashBoxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Collections_BankAccounts_BankAccountId",
                table: "Collections");

            migrationBuilder.DropForeignKey(
                name: "FK_Collections_CashBoxes_CashBoxId",
                table: "Collections");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerTransactions_BankAccounts_BankAccountId",
                table: "LedgerTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerTransactions_CashBoxes_CashBoxId",
                table: "LedgerTransactions");

            migrationBuilder.DropTable(
                name: "BankAccounts");

            migrationBuilder.DropTable(
                name: "CashBoxes");

            migrationBuilder.DropIndex(
                name: "IX_LedgerTransactions_BankAccountId",
                table: "LedgerTransactions");

            migrationBuilder.DropIndex(
                name: "IX_LedgerTransactions_CashBoxId",
                table: "LedgerTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Collections_BankAccountId",
                table: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Collections_CashBoxId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "CashBoxId",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "CashBoxId",
                table: "Collections");
        }
    }
}
