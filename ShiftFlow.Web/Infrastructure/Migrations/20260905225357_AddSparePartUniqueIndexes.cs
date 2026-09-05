using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSparePartUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SpareParts_Name",
                table: "SpareParts");

            migrationBuilder.CreateIndex(
                name: "IX_SpareParts_Name",
                table: "SpareParts",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpareParts_Sku",
                table: "SpareParts",
                column: "Sku",
                unique: true,
                filter: "[Sku] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SpareParts_Name",
                table: "SpareParts");

            migrationBuilder.DropIndex(
                name: "IX_SpareParts_Sku",
                table: "SpareParts");

            migrationBuilder.CreateIndex(
                name: "IX_SpareParts_Name",
                table: "SpareParts",
                column: "Name");
        }
    }
}
