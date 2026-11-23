using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kumburgaz.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferGroupId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TransferGroupId",
                table: "LedgerTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_TransferGroupId",
                table: "LedgerTransactions",
                column: "TransferGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerTransactions_TransferGroupId",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "TransferGroupId",
                table: "LedgerTransactions");
        }
    }
}
