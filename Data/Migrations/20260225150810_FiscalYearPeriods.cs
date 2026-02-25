using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class FiscalYearPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Period",
                table: "DuesInstallments",
                type: "character varying(9)",
                maxLength: 9,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7);

            migrationBuilder.AlterColumn<string>(
                name: "StartPeriod",
                table: "BillingGroupUnits",
                type: "character varying(9)",
                maxLength: 9,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7);

            migrationBuilder.AlterColumn<string>(
                name: "EndPeriod",
                table: "BillingGroupUnits",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EffectiveStartPeriod",
                table: "BillingGroups",
                type: "character varying(9)",
                maxLength: 9,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7);

            migrationBuilder.AlterColumn<string>(
                name: "EffectiveEndPeriod",
                table: "BillingGroups",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldNullable: true);

            migrationBuilder.Sql("""
                UPDATE "DuesInstallments"
                SET "Period" = CASE
                    WHEN length("Period") = 7 THEN
                        CASE
                            WHEN CAST(split_part("Period", '-', 2) AS integer) >= 7
                                THEN split_part("Period", '-', 1) || '-' || (CAST(split_part("Period", '-', 1) AS integer) + 1)::text
                            ELSE (CAST(split_part("Period", '-', 1) AS integer) - 1)::text || '-' || split_part("Period", '-', 1)
                        END
                    ELSE "Period"
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "BillingGroups"
                SET "EffectiveStartPeriod" = CASE
                    WHEN length("EffectiveStartPeriod") = 7 THEN
                        CASE
                            WHEN CAST(split_part("EffectiveStartPeriod", '-', 2) AS integer) >= 7
                                THEN split_part("EffectiveStartPeriod", '-', 1) || '-' || (CAST(split_part("EffectiveStartPeriod", '-', 1) AS integer) + 1)::text
                            ELSE (CAST(split_part("EffectiveStartPeriod", '-', 1) AS integer) - 1)::text || '-' || split_part("EffectiveStartPeriod", '-', 1)
                        END
                    ELSE "EffectiveStartPeriod"
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "BillingGroups"
                SET "EffectiveEndPeriod" = CASE
                    WHEN "EffectiveEndPeriod" IS NULL THEN NULL
                    WHEN length("EffectiveEndPeriod") = 7 THEN
                        CASE
                            WHEN CAST(split_part("EffectiveEndPeriod", '-', 2) AS integer) >= 7
                                THEN split_part("EffectiveEndPeriod", '-', 1) || '-' || (CAST(split_part("EffectiveEndPeriod", '-', 1) AS integer) + 1)::text
                            ELSE (CAST(split_part("EffectiveEndPeriod", '-', 1) AS integer) - 1)::text || '-' || split_part("EffectiveEndPeriod", '-', 1)
                        END
                    ELSE "EffectiveEndPeriod"
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "BillingGroupUnits"
                SET "StartPeriod" = CASE
                    WHEN length("StartPeriod") = 7 THEN
                        CASE
                            WHEN CAST(split_part("StartPeriod", '-', 2) AS integer) >= 7
                                THEN split_part("StartPeriod", '-', 1) || '-' || (CAST(split_part("StartPeriod", '-', 1) AS integer) + 1)::text
                            ELSE (CAST(split_part("StartPeriod", '-', 1) AS integer) - 1)::text || '-' || split_part("StartPeriod", '-', 1)
                        END
                    ELSE "StartPeriod"
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "BillingGroupUnits"
                SET "EndPeriod" = CASE
                    WHEN "EndPeriod" IS NULL THEN NULL
                    WHEN length("EndPeriod") = 7 THEN
                        CASE
                            WHEN CAST(split_part("EndPeriod", '-', 2) AS integer) >= 7
                                THEN split_part("EndPeriod", '-', 1) || '-' || (CAST(split_part("EndPeriod", '-', 1) AS integer) + 1)::text
                            ELSE (CAST(split_part("EndPeriod", '-', 1) AS integer) - 1)::text || '-' || split_part("EndPeriod", '-', 1)
                        END
                    ELSE "EndPeriod"
                END;
                """);

            migrationBuilder.UpdateData(
                table: "BillingGroupUnits",
                keyColumn: "Id",
                keyValue: 1,
                column: "StartPeriod",
                value: "2025-2026");

            migrationBuilder.UpdateData(
                table: "BillingGroupUnits",
                keyColumn: "Id",
                keyValue: 2,
                column: "StartPeriod",
                value: "2025-2026");

            migrationBuilder.UpdateData(
                table: "BillingGroupUnits",
                keyColumn: "Id",
                keyValue: 3,
                column: "StartPeriod",
                value: "2025-2026");

            migrationBuilder.UpdateData(
                table: "BillingGroups",
                keyColumn: "Id",
                keyValue: 1,
                column: "EffectiveStartPeriod",
                value: "2025-2026");

            migrationBuilder.UpdateData(
                table: "BillingGroups",
                keyColumn: "Id",
                keyValue: 2,
                column: "EffectiveStartPeriod",
                value: "2025-2026");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Period",
                table: "DuesInstallments",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(9)",
                oldMaxLength: 9);

            migrationBuilder.AlterColumn<string>(
                name: "StartPeriod",
                table: "BillingGroupUnits",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(9)",
                oldMaxLength: 9);

            migrationBuilder.AlterColumn<string>(
                name: "EndPeriod",
                table: "BillingGroupUnits",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(9)",
                oldMaxLength: 9,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EffectiveStartPeriod",
                table: "BillingGroups",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(9)",
                oldMaxLength: 9);

            migrationBuilder.AlterColumn<string>(
                name: "EffectiveEndPeriod",
                table: "BillingGroups",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(9)",
                oldMaxLength: 9,
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "BillingGroupUnits",
                keyColumn: "Id",
                keyValue: 1,
                column: "StartPeriod",
                value: "2026-01");

            migrationBuilder.UpdateData(
                table: "BillingGroupUnits",
                keyColumn: "Id",
                keyValue: 2,
                column: "StartPeriod",
                value: "2026-01");

            migrationBuilder.UpdateData(
                table: "BillingGroupUnits",
                keyColumn: "Id",
                keyValue: 3,
                column: "StartPeriod",
                value: "2026-01");

            migrationBuilder.UpdateData(
                table: "BillingGroups",
                keyColumn: "Id",
                keyValue: 1,
                column: "EffectiveStartPeriod",
                value: "2026-01");

            migrationBuilder.UpdateData(
                table: "BillingGroups",
                keyColumn: "Id",
                keyValue: 2,
                column: "EffectiveStartPeriod",
                value: "2026-01");
        }
    }
}
