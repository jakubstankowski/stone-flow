using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Expenses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OwnerWithdrawals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OwnerWithdrawals",
                schema: "expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Person = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WithdrawnOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreationRequestHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerWithdrawals", x => x.Id);
                    table.CheckConstraint("CK_OwnerWithdrawals_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_OwnerWithdrawals_CreationRequest", "([CreationRequestId] IS NULL AND [CreationRequestHash] IS NULL) OR ([CreationRequestId] IS NOT NULL AND [CreationRequestHash] IS NOT NULL)");
                    table.CheckConstraint("CK_OwnerWithdrawals_Method", "[Method] IN (N'Transfer', N'Blik', N'Cash')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerWithdrawals_WithdrawnOn",
                schema: "expenses",
                table: "OwnerWithdrawals",
                column: "WithdrawnOn")
                .Annotation("SqlServer:Include", new[] { "Amount" });

            migrationBuilder.CreateIndex(
                name: "UX_OwnerWithdrawals_CreationRequestId",
                schema: "expenses",
                table: "OwnerWithdrawals",
                column: "CreationRequestId",
                unique: true,
                filter: "[CreationRequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnerWithdrawals",
                schema: "expenses");
        }
    }
}
