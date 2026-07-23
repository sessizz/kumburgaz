using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditImportSoftDeleteAndBackups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Units",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "Units",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "Units",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Units",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "UnitAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "UnitAccounts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "UnitAccounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "UnitAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "LedgerTransactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "LedgerTransactions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "LedgerTransactions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "LedgerTransactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "IncomeExpenseCategories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "IncomeExpenseCategories",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "IncomeExpenseCategories",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "IncomeExpenseCategories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "DuesTypes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "DuesTypes",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "DuesTypes",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "DuesTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "DuesInstallments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "DuesInstallments",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "DuesInstallments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "DuesInstallments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "CombinedUnitMembers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "CombinedUnitMembers",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "CombinedUnitMembers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "CombinedUnitMembers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Collections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "Collections",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "Collections",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Collections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "CollectionAllocations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "CollectionAllocations",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "CollectionAllocations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "CollectionAllocations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "CashBoxes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "CashBoxes",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "CashBoxes",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "CashBoxes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Blocks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "Blocks",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "Blocks",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Blocks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "BillingGroupUnits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "BillingGroupUnits",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "BillingGroupUnits",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "BillingGroupUnits",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "BillingGroups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "BillingGroups",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "BillingGroups",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "BillingGroups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "BankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "BankAccounts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "BankAccounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "BankAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "Accounts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserName",
                table: "Accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntityName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    OldValuesJson = table.Column<string>(type: "text", nullable: true),
                    NewValuesJson = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsistencyCheckResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CheckName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EntityName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    EntityId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Resolved = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsistencyCheckResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportNo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceAccountKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SourceAccountId = table.Column<int>(type: "integer", nullable: true),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    FileHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CommittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RolledBackAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatchRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportBatchId = table.Column<int>(type: "integer", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    RawJson = table.Column<string>(type: "text", nullable: false),
                    NormalizedKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedEntityName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedEntityId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatchRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatchRows_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "BillingGroupUnits",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "BillingGroupUnits",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "BillingGroupUnits",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "BillingGroups",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "BillingGroups",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "Blocks",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "Blocks",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "Blocks",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "CashBoxes",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "DuesTypes",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "DuesTypes",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "IncomeExpenseCategories",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "IncomeExpenseCategories",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "IncomeExpenseCategories",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "Units",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "Units",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.UpdateData(
                table: "Units",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DeletedAt", "DeletedByUserId", "DeletedByUserName", "IsDeleted" },
                values: new object[] { null, null, null, false });

            migrationBuilder.CreateIndex(
                name: "IX_Units_IsDeleted",
                table: "Units",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_UnitAccounts_IsDeleted",
                table: "UnitAccounts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_IsDeleted",
                table: "LedgerTransactions",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_IncomeExpenseCategories_IsDeleted",
                table: "IncomeExpenseCategories",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_DuesTypes_IsDeleted",
                table: "DuesTypes",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_DuesInstallments_IsDeleted",
                table: "DuesInstallments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CombinedUnitMembers_IsDeleted",
                table: "CombinedUnitMembers",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_IsDeleted",
                table: "Collections",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionAllocations_IsDeleted",
                table: "CollectionAllocations",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_CashBoxes_IsDeleted",
                table: "CashBoxes",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_IsDeleted",
                table: "Blocks",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_BillingGroupUnits_IsDeleted",
                table: "BillingGroupUnits",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_BillingGroups_IsDeleted",
                table: "BillingGroups",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_IsDeleted",
                table: "BankAccounts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_IsDeleted",
                table: "Accounts",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedAt",
                table: "AuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityName_EntityId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "EntityName", "EntityId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsistencyCheckResults_Resolved_Severity_CreatedAt",
                table: "ConsistencyCheckResults",
                columns: new[] { "Resolved", "Severity", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_ImportNo",
                table: "ImportBatches",
                column: "ImportNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_Type_Status_CreatedAt",
                table: "ImportBatches",
                columns: new[] { "Type", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatchRows_ImportBatchId_LineNo",
                table: "ImportBatchRows",
                columns: new[] { "ImportBatchId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatchRows_NormalizedKey",
                table: "ImportBatchRows",
                column: "NormalizedKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "ConsistencyCheckResults");

            migrationBuilder.DropTable(
                name: "ImportBatchRows");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_Units_IsDeleted",
                table: "Units");

            migrationBuilder.DropIndex(
                name: "IX_UnitAccounts_IsDeleted",
                table: "UnitAccounts");

            migrationBuilder.DropIndex(
                name: "IX_LedgerTransactions_IsDeleted",
                table: "LedgerTransactions");

            migrationBuilder.DropIndex(
                name: "IX_IncomeExpenseCategories_IsDeleted",
                table: "IncomeExpenseCategories");

            migrationBuilder.DropIndex(
                name: "IX_DuesTypes_IsDeleted",
                table: "DuesTypes");

            migrationBuilder.DropIndex(
                name: "IX_DuesInstallments_IsDeleted",
                table: "DuesInstallments");

            migrationBuilder.DropIndex(
                name: "IX_CombinedUnitMembers_IsDeleted",
                table: "CombinedUnitMembers");

            migrationBuilder.DropIndex(
                name: "IX_Collections_IsDeleted",
                table: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_CollectionAllocations_IsDeleted",
                table: "CollectionAllocations");

            migrationBuilder.DropIndex(
                name: "IX_CashBoxes_IsDeleted",
                table: "CashBoxes");

            migrationBuilder.DropIndex(
                name: "IX_Blocks_IsDeleted",
                table: "Blocks");

            migrationBuilder.DropIndex(
                name: "IX_BillingGroupUnits_IsDeleted",
                table: "BillingGroupUnits");

            migrationBuilder.DropIndex(
                name: "IX_BillingGroups_IsDeleted",
                table: "BillingGroups");

            migrationBuilder.DropIndex(
                name: "IX_BankAccounts_IsDeleted",
                table: "BankAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_IsDeleted",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "UnitAccounts");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "UnitAccounts");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "UnitAccounts");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "UnitAccounts");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "IncomeExpenseCategories");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "IncomeExpenseCategories");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "IncomeExpenseCategories");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "IncomeExpenseCategories");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "DuesTypes");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "DuesTypes");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "DuesTypes");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "DuesTypes");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "DuesInstallments");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "DuesInstallments");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "DuesInstallments");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "DuesInstallments");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CombinedUnitMembers");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "CombinedUnitMembers");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "CombinedUnitMembers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "CombinedUnitMembers");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CollectionAllocations");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "CollectionAllocations");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "CollectionAllocations");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "CollectionAllocations");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CashBoxes");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "CashBoxes");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "CashBoxes");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "CashBoxes");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Blocks");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Blocks");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "Blocks");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Blocks");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "BillingGroupUnits");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "BillingGroupUnits");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "BillingGroupUnits");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "BillingGroupUnits");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "BillingGroups");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "BillingGroups");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "BillingGroups");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "BillingGroups");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "DeletedByUserName",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Accounts");
        }
    }
}
