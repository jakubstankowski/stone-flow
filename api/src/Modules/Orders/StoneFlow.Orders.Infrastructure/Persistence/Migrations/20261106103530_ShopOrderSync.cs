using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShopOrderSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ShopDeletedAtUtc",
                schema: "orders",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShopModifiedAtUtc",
                schema: "orders",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShopRefundedAmount",
                schema: "orders",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ShopDeletions",
                schema: "orders",
                columns: table => new
                {
                    ExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopDeletions", x => x.ExternalId);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_ShopFields",
                schema: "orders",
                table: "Orders",
                sql: "[Source] <> N'Manual' OR ([ShopModifiedAtUtc] IS NULL AND [ShopDeletedAtUtc] IS NULL AND [ShopRefundedAmount] = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_ShopRefundedAmount",
                schema: "orders",
                table: "Orders",
                sql: "[ShopRefundedAmount] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShopDeletions",
                schema: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_ShopFields",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_ShopRefundedAmount",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShopDeletedAtUtc",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShopModifiedAtUtc",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShopRefundedAmount",
                schema: "orders",
                table: "Orders");
        }
    }
}
