using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrdersAndNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "OrderNumberCounters",
                schema: "orders",
                columns: table => new
                {
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderNumberCounters", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FulfillmentStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ShopStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreationRequestHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CustomerCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    CustomerKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CustomerPostalCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    CustomerStreet = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CustomerTaxId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    HasPhotos = table.Column<bool>(type: "bit", nullable: false),
                    HasReview = table.Column<bool>(type: "bit", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Suppliers = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ZoReferences = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.CheckConstraint("CK_Orders_Source_ExternalId", "([Source] = N'Manual' AND [ExternalId] IS NULL) OR ([Source] <> N'Manual' AND [ExternalId] IS NOT NULL)");
                    table.CheckConstraint("CK_Orders_Value", "[Value] >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Channel",
                schema: "orders",
                table: "Orders",
                column: "Channel");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreationRequestId",
                schema: "orders",
                table: "Orders",
                column: "CreationRequestId",
                unique: true,
                filter: "[CreationRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_FulfillmentStatus",
                schema: "orders",
                table: "Orders",
                column: "FulfillmentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Number",
                schema: "orders",
                table: "Orders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderDate_Id",
                schema: "orders",
                table: "Orders",
                columns: new[] { "OrderDate", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Source_ExternalId",
                schema: "orders",
                table: "Orders",
                columns: new[] { "Source", "ExternalId" },
                unique: true,
                filter: "[ExternalId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderNumberCounters",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "Orders",
                schema: "orders");
        }
    }
}
