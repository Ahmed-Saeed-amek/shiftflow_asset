using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase38_OrderTypeIsDirectFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDirectFix",
                table: "OrderTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Standard (Id=3) is the type Maintenance Orders already default to - mark it
            // maintenance-style so existing behavior is preserved exactly once IsDirectFix starts
            // driving routing on the new unified Orders/Create screen.
            migrationBuilder.UpdateData(
                table: "OrderTypes",
                keyColumn: "Id",
                keyValue: 3,
                column: "IsDirectFix",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDirectFix",
                table: "OrderTypes");
        }
    }
}
