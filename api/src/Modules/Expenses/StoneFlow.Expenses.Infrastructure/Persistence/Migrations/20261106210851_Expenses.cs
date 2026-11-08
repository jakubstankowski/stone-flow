using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Expenses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Expenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "expenses");

            migrationBuilder.CreateTable(
                name: "Expenses",
                schema: "expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Counterparty = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CostDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DocumentReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Payer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreationRequestHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.CheckConstraint("CK_Expenses_CreationRequest", "([CreationRequestId] IS NULL AND [CreationRequestHash] IS NULL) OR ([CreationRequestId] IS NOT NULL AND [CreationRequestHash] IS NOT NULL)");
                    table.CheckConstraint("CK_Expenses_GrossAmount", "[GrossAmount] <> 0");
                    table.CheckConstraint("CK_Expenses_NetAmount", "[NetAmount] IS NULL OR (ABS([NetAmount]) <= ABS([GrossAmount]) AND ([NetAmount] = 0 OR SIGN([NetAmount]) = SIGN([GrossAmount])))");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CostDate",
                schema: "expenses",
                table: "Expenses",
                columns: new[] { "CostDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_PaidOn",
                schema: "expenses",
                table: "Expenses",
                columns: new[] { "PaidOn", "Id" },
                filter: "[PaidOn] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Expenses_CreationRequestId",
                schema: "expenses",
                table: "Expenses",
                column: "CreationRequestId",
                unique: true,
                filter: "[CreationRequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Expenses",
                schema: "expenses");
        }
    }
}
