using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncementsRequestsDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Announcements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PublishDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announcements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DocumentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UnitId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    AssignedTo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceRequests_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "Announcements",
                columns: new[] { "Id", "Body", "IsPublished", "Priority", "PublishDate", "Title" },
                values: new object[,]
                {
                    { 1, "26 Mayıs Pazar günü havuzumuz bakım nedeniyle kapalı olacaktır.", true, "Önemli", new DateTime(2026, 5, 1, 9, 0, 0, 0, DateTimeKind.Utc), "Havuz Bakım Çalışması" },
                    { 2, "Ortak alan aydınlatmalarında yenileme çalışmaları başlamıştır.", true, "Normal", new DateTime(2026, 5, 3, 9, 0, 0, 0, DateTimeKind.Utc), "Ortak Alan Aydınlatmaları" }
                });

            migrationBuilder.InsertData(
                table: "DocumentRecords",
                columns: new[] { "Id", "Category", "DocumentDate", "Note", "Title", "Url" },
                values: new object[,]
                {
                    { 1, "Toplantı", new DateTime(2026, 1, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Genel kurul karar özeti.", "2026 Genel Kurul Tutanağı", "" },
                    { 2, "Sözleşme", new DateTime(2026, 2, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Yıllık güvenlik hizmet sözleşmesi.", "Güvenlik Hizmeti Sözleşmesi", "" }
                });

            migrationBuilder.InsertData(
                table: "ServiceRequests",
                columns: new[] { "Id", "AssignedTo", "CreatedAt", "Description", "DueDate", "Priority", "ResolvedAt", "Status", "Title", "UnitId" },
                values: new object[,]
                {
                    { 1, "Teknik Servis", new DateTime(2026, 5, 7, 8, 30, 0, 0, DateTimeKind.Utc), "A Blok asansörü katta kalıyor.", new DateTime(2026, 5, 10, 18, 0, 0, 0, DateTimeKind.Utc), 4, null, 1, "Asansör arızası - A Blok", 1 },
                    { 2, "Site Görevlisi", new DateTime(2026, 5, 6, 11, 0, 0, 0, DateTimeKind.Utc), "Ortak bahçe aydınlatması kontrol edilecek.", null, 2, null, 2, "Bahçe aydınlatması", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_PublishDate",
                table: "Announcements",
                column: "PublishDate");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRecords_Category_DocumentDate",
                table: "DocumentRecords",
                columns: new[] { "Category", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Status_Priority_CreatedAt",
                table: "ServiceRequests",
                columns: new[] { "Status", "Priority", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_UnitId",
                table: "ServiceRequests",
                column: "UnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Announcements");

            migrationBuilder.DropTable(
                name: "DocumentRecords");

            migrationBuilder.DropTable(
                name: "ServiceRequests");
        }
    }
}
