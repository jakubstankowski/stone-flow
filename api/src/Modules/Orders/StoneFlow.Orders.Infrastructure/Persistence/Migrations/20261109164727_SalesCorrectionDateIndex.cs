using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesCorrectionDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SalesCorrections_CorrectedOn",
                schema: "orders",
                table: "SalesCorrections",
                column: "CorrectedOn")
                .Annotation("SqlServer:Include", new[] { "Amount", "OrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesCorrections_CorrectedOn",
                schema: "orders",
                table: "SalesCorrections");
        }
    }
}
