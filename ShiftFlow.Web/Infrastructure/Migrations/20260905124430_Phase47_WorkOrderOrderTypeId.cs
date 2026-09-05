using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase47_WorkOrderOrderTypeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrderTypeId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrderTypeId",
                table: "WorkOrders",
                column: "OrderTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_OrderTypes_OrderTypeId",
                table: "WorkOrders",
                column: "OrderTypeId",
                principalTable: "OrderTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_OrderTypes_OrderTypeId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_OrderTypeId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "OrderTypeId",
                table: "WorkOrders");
        }
    }
}
