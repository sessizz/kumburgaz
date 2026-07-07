using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReportLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Section = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Visible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportLineCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportLineId = table.Column<int>(type: "integer", nullable: false),
                    IncomeExpenseCategoryId = table.Column<int>(type: "integer", nullable: true),
                    IsDuesCollections = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportLineCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportLineCategories_IncomeExpenseCategories_IncomeExpenseC~",
                        column: x => x.IncomeExpenseCategoryId,
                        principalTable: "IncomeExpenseCategories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReportLineCategories_ReportLines_ReportLineId",
                        column: x => x.ReportLineId,
                        principalTable: "ReportLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportLineCategories_IncomeExpenseCategoryId",
                table: "ReportLineCategories",
                column: "IncomeExpenseCategoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportLineCategories_IsDuesCollections",
                table: "ReportLineCategories",
                column: "IsDuesCollections",
                unique: true,
                filter: "\"IsDuesCollections\"");

            migrationBuilder.CreateIndex(
                name: "IX_ReportLineCategories_ReportLineId",
                table: "ReportLineCategories",
                column: "ReportLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportLineCategories");

            migrationBuilder.DropTable(
                name: "ReportLines");
        }
    }
}
