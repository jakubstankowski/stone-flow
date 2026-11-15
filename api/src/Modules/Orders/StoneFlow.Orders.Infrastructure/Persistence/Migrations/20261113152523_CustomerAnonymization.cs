using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerAnonymization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnonymizedAtUtc",
                schema: "orders",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AnonymizedByAccountId",
                schema: "orders",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Anonymization",
                schema: "orders",
                table: "Orders",
                sql: "([AnonymizedAtUtc] IS NULL AND [AnonymizedByAccountId] IS NULL) OR ([AnonymizedAtUtc] IS NOT NULL AND [AnonymizedByAccountId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Anonymization",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AnonymizedAtUtc",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AnonymizedByAccountId",
                schema: "orders",
                table: "Orders");
        }
    }
}
