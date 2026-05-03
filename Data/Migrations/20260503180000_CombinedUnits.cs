using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CombinedUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCombined",
                table: "Units",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CombinedUnitMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CombinedUnitId = table.Column<int>(type: "integer", nullable: false),
                    ComponentUnitId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CombinedUnitMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CombinedUnitMembers_Units_CombinedUnitId",
                        column: x => x.CombinedUnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CombinedUnitMembers_Units_ComponentUnitId",
                        column: x => x.ComponentUnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CombinedUnitMembers_ComponentUnitId",
                table: "CombinedUnitMembers",
                column: "ComponentUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_CombinedUnitMembers_CombinedUnitId_ComponentUnitId",
                table: "CombinedUnitMembers",
                columns: new[] { "CombinedUnitId", "ComponentUnitId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CombinedUnitMembers");

            migrationBuilder.DropColumn(
                name: "IsCombined",
                table: "Units");
        }
    }
}
