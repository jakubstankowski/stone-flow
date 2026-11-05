using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesCorrections",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CorrectedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreationRequestHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesCorrections", x => x.Id);
                    table.CheckConstraint("CK_SalesCorrections_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_SalesCorrections_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "orders",
                        principalTable: "Orders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesCorrections_CreationRequestId",
                schema: "orders",
                table: "SalesCorrections",
                column: "CreationRequestId",
                unique: true,
                filter: "[CreationRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesCorrections_OrderId",
                schema: "orders",
                table: "SalesCorrections",
                column: "OrderId")
                .Annotation("SqlServer:Include", new[] { "Amount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesCorrections",
                schema: "orders");
        }
    }
}
