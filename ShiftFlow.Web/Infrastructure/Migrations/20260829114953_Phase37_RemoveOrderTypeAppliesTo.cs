using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase37_RemoveOrderTypeAppliesTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderTypes_AppliesTo_Name",
                table: "OrderTypes");

            migrationBuilder.DropColumn(
                name: "AppliesTo",
                table: "OrderTypes");

            migrationBuilder.CreateIndex(
                name: "IX_OrderTypes_Name",
                table: "OrderTypes",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderTypes_Name",
                table: "OrderTypes");

            migrationBuilder.AddColumn<string>(
                name: "AppliesTo",
                table: "OrderTypes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_OrderTypes_AppliesTo_Name",
                table: "OrderTypes",
                columns: new[] { "AppliesTo", "Name" },
                unique: true);
        }
    }
}
