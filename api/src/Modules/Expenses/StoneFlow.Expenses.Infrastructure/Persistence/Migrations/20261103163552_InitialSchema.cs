using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoneFlow.Expenses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "expenses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Schemat zostaje: zawiera tabelę historii migracji tego modułu.
        }
    }
}
