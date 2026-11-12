using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberHub.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelWithEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF NOT EXISTS: databases created outside of migrations may already have these columns.
            migrationBuilder.Sql(@"ALTER TABLE ""Products"" ADD COLUMN IF NOT EXISTS ""CostPrice"" numeric(10,2) NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""OrderItems"" ADD COLUMN IF NOT EXISTS ""UnitCostPrice"" numeric(10,2) NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostPrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UnitCostPrice",
                table: "OrderItems");
        }
    }
}
