using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountsAndDuesResponsibleAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResponsibleAccountId",
                table: "DuesInstallments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    AccountType = table.Column<int>(type: "integer", nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnitAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UnitId = table.Column<int>(type: "integer", nullable: false),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitAccounts_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitAccounts_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DuesInstallments_ResponsibleAccountId",
                table: "DuesInstallments",
                column: "ResponsibleAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_AccountType_Name",
                table: "Accounts",
                columns: new[] { "AccountType", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitAccounts_AccountId",
                table: "UnitAccounts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitAccounts_UnitId_Role_Active",
                table: "UnitAccounts",
                columns: new[] { "UnitId", "Role", "Active" });

            migrationBuilder.Sql("""
                INSERT INTO "Accounts" ("Name", "AccountType", "Phone", "Email", "Note", "Active")
                SELECT DISTINCT trim("OwnerName"), 1, NULL, NULL, 'Daire malik bilgisinden aktarıldı.', TRUE
                FROM "Units"
                WHERE "OwnerName" IS NOT NULL AND trim("OwnerName") <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM "Accounts"
                      WHERE "AccountType" = 1 AND lower("Accounts"."Name") = lower(trim("Units"."OwnerName"))
                  );
                """);

            migrationBuilder.Sql("""
                INSERT INTO "UnitAccounts" ("UnitId", "AccountId", "Role", "Active", "StartDate", "EndDate")
                SELECT u."Id", a."Id", 1, TRUE, u."MoveInDate", NULL
                FROM "Units" u
                JOIN "Accounts" a ON a."AccountType" = 1 AND lower(a."Name") = lower(trim(u."OwnerName"))
                WHERE u."OwnerName" IS NOT NULL AND trim(u."OwnerName") <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM "UnitAccounts" ua
                      WHERE ua."UnitId" = u."Id" AND ua."AccountId" = a."Id" AND ua."Role" = 1
                  );
                """);

            migrationBuilder.Sql("""
                UPDATE "DuesInstallments" di
                SET "ResponsibleAccountId" = ua."AccountId"
                FROM "UnitAccounts" ua
                WHERE ua."Role" = 1
                  AND ua."Active" = TRUE
                  AND di."ResponsibleAccountId" IS NULL
                  AND di."UnitId" = ua."UnitId";
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_DuesInstallments_Accounts_ResponsibleAccountId",
                table: "DuesInstallments",
                column: "ResponsibleAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DuesInstallments_Accounts_ResponsibleAccountId",
                table: "DuesInstallments");

            migrationBuilder.DropTable(
                name: "UnitAccounts");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_DuesInstallments_ResponsibleAccountId",
                table: "DuesInstallments");

            migrationBuilder.DropColumn(
                name: "ResponsibleAccountId",
                table: "DuesInstallments");
        }
    }
}
