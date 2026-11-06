using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Integrations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WooCommerceBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integrations");

            migrationBuilder.CreateTable(
                name: "WooCommerceOrderErrors",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FirstOccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastOccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Context = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FailedModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedBy = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WooCommerceOrderErrors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WooCommerceSyncRuns",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OrdersProcessed = table.Column<int>(type: "int", nullable: false),
                    Errors = table.Column<int>(type: "int", nullable: false),
                    Unidentified = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WooCommerceSyncRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WooCommerceSyncState",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LastSuccessCursorUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WooCommerceSyncState", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WooCommerceOrderErrors_ExternalId",
                schema: "integrations",
                table: "WooCommerceOrderErrors",
                column: "ExternalId",
                unique: true,
                filter: "[Status] = N'open'");

            migrationBuilder.CreateIndex(
                name: "IX_WooCommerceOrderErrors_LastOccurredAtUtc",
                schema: "integrations",
                table: "WooCommerceOrderErrors",
                column: "LastOccurredAtUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_WooCommerceSyncRuns_StartedAtUtc",
                schema: "integrations",
                table: "WooCommerceSyncRuns",
                column: "StartedAtUtc",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WooCommerceOrderErrors",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "WooCommerceSyncRuns",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "WooCommerceSyncState",
                schema: "integrations");
        }
    }
}
