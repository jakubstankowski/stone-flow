using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditEntriesBusinessDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ObjectBusinessDate",
                schema: "reporting",
                table: "AuditEntries",
                column: "ObjectBusinessDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_ObjectBusinessDate",
                schema: "reporting",
                table: "AuditEntries");
        }
    }
}
