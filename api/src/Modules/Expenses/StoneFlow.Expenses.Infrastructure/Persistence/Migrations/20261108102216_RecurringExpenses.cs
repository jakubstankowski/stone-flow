using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Expenses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecurringExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecurringDefinitionId",
                schema: "expenses",
                table: "Expenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RecurringPeriod",
                schema: "expenses",
                table: "Expenses",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringDefinitionRequests",
                schema: "expenses",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatedOccurrences = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringDefinitionRequests", x => x.Key);
                    table.CheckConstraint("CK_RecurringDefinitionRequests_CreatedOccurrences", "[CreatedOccurrences] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "RecurringExpenseDefinitions",
                schema: "expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Counterparty = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Payer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DayOfMonth = table.Column<byte>(type: "tinyint", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LastDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PausedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringExpenseDefinitions", x => x.Id);
                    table.CheckConstraint("CK_RecurringExpenseDefinitions_DayOfMonth", "[DayOfMonth] BETWEEN 1 AND 31");
                    table.CheckConstraint("CK_RecurringExpenseDefinitions_EndsOn", "[EndsOn] IS NULL OR [EndsOn] >= [StartsOn]");
                    table.CheckConstraint("CK_RecurringExpenseDefinitions_GrossAmount", "[GrossAmount] > 0");
                    table.CheckConstraint("CK_RecurringExpenseDefinitions_LastDueDate", "([EndsOn] IS NULL AND [LastDueDate] IS NULL) OR ([EndsOn] IS NOT NULL AND [LastDueDate] IS NOT NULL AND [LastDueDate] >= [StartsOn] AND [LastDueDate] <= [EndsOn])");
                    table.CheckConstraint("CK_RecurringExpenseDefinitions_NetAmount", "[NetAmount] IS NULL OR ([NetAmount] >= 0 AND [NetAmount] <= [GrossAmount])");
                });

            migrationBuilder.CreateTable(
                name: "RecurringExpenseDays",
                schema: "expenses",
                columns: table => new
                {
                    FromPeriod = table.Column<DateOnly>(type: "date", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfMonth = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringExpenseDays", x => new { x.DefinitionId, x.FromPeriod });
                    table.CheckConstraint("CK_RecurringExpenseDays_DayOfMonth", "[DayOfMonth] BETWEEN 1 AND 31");
                    table.CheckConstraint("CK_RecurringExpenseDays_FromPeriod", "DAY([FromPeriod]) = 1");
                    table.ForeignKey(
                        name: "FK_RecurringExpenseDays_RecurringExpenseDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "expenses",
                        principalTable: "RecurringExpenseDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringExpenseExclusions",
                schema: "expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringExpenseExclusions", x => x.Id);
                    table.CheckConstraint("CK_RecurringExpenseExclusions_Range", "[ToDate] > [FromDate]");
                    table.CheckConstraint("CK_RecurringExpenseExclusions_Reason", "[Reason] IN ('Pause', 'StartMoved')");
                    table.ForeignKey(
                        name: "FK_RecurringExpenseExclusions_RecurringExpenseDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "expenses",
                        principalTable: "RecurringExpenseDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringExpensePeriods",
                schema: "expenses",
                columns: table => new
                {
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Period = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringExpensePeriods", x => new { x.DefinitionId, x.Period });
                    table.CheckConstraint("CK_RecurringExpensePeriods_Period", "DAY([Period]) = 1");
                    table.ForeignKey(
                        name: "FK_RecurringExpensePeriods_RecurringExpenseDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "expenses",
                        principalTable: "RecurringExpenseDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_RecurringDefinitionId",
                schema: "expenses",
                table: "Expenses",
                columns: new[] { "RecurringDefinitionId", "CostDate", "Id" },
                filter: "[RecurringDefinitionId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Expenses_Recurring",
                schema: "expenses",
                table: "Expenses",
                sql: "([RecurringDefinitionId] IS NULL AND [RecurringPeriod] IS NULL) OR ([RecurringDefinitionId] IS NOT NULL AND [RecurringPeriod] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringDefinitionRequests_DefinitionId",
                schema: "expenses",
                table: "RecurringDefinitionRequests",
                column: "DefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenseDefinitions_Description",
                schema: "expenses",
                table: "RecurringExpenseDefinitions",
                columns: new[] { "Description", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenseDefinitions_Generation",
                schema: "expenses",
                table: "RecurringExpenseDefinitions",
                columns: new[] { "PausedOn", "EndsOn" })
                .Annotation("SqlServer:Include", new[] { "StartsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenseExclusions_DefinitionId",
                schema: "expenses",
                table: "RecurringExpenseExclusions",
                column: "DefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_RecurringExpensePeriods_ExpenseId",
                schema: "expenses",
                table: "RecurringExpensePeriods",
                column: "ExpenseId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringDefinitionRequests",
                schema: "expenses");

            migrationBuilder.DropTable(
                name: "RecurringExpenseDays",
                schema: "expenses");

            migrationBuilder.DropTable(
                name: "RecurringExpenseExclusions",
                schema: "expenses");

            migrationBuilder.DropTable(
                name: "RecurringExpensePeriods",
                schema: "expenses");

            migrationBuilder.DropTable(
                name: "RecurringExpenseDefinitions",
                schema: "expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_RecurringDefinitionId",
                schema: "expenses",
                table: "Expenses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Expenses_Recurring",
                schema: "expenses",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "RecurringDefinitionId",
                schema: "expenses",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "RecurringPeriod",
                schema: "expenses",
                table: "Expenses");
        }
    }
}
