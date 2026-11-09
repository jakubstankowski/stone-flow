using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashOperationDateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Refunds_RefundedOn",
                schema: "orders",
                table: "Refunds",
                column: "RefundedOn")
                .Annotation("SqlServer:Include", new[] { "Amount", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaidOn",
                schema: "orders",
                table: "Payments",
                column: "PaidOn")
                .Annotation("SqlServer:Include", new[] { "Amount", "OrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Refunds_RefundedOn",
                schema: "orders",
                table: "Refunds");

            migrationBuilder.DropIndex(
                name: "IX_Payments_PaidOn",
                schema: "orders",
                table: "Payments");
        }
    }
}
