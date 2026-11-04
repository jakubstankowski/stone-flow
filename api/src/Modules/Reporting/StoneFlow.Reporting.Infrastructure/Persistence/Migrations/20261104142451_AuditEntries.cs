using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reporting");

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                schema: "reporting",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AuthorKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AuthorAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ObjectType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObjectBusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ObjectLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentObjectType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ParentObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Operation = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Changes = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                    table.CheckConstraint("CK_AuditEntries_Author", "([AuthorKind] = N'Account' AND [AuthorAccountId] IS NOT NULL) OR ([AuthorKind] <> N'Account' AND [AuthorAccountId] IS NULL)");
                    table.CheckConstraint("CK_AuditEntries_Parent", "([ParentObjectType] IS NULL AND [ParentObjectId] IS NULL) OR ([ParentObjectType] IS NOT NULL AND [ParentObjectId] IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ObjectType_ObjectId",
                schema: "reporting",
                table: "AuditEntries",
                columns: new[] { "ObjectType", "ObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                schema: "reporting",
                table: "AuditEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ParentObjectType_ParentObjectId",
                schema: "reporting",
                table: "AuditEntries",
                columns: new[] { "ParentObjectType", "ParentObjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries",
                schema: "reporting");
        }
    }
}
