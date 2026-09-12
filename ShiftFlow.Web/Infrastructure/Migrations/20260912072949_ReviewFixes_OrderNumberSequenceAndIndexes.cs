using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReviewFixes_OrderNumberSequenceAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderNumberSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Prefix = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastValue = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderNumberSequences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CreatedDate",
                table: "WorkOrders",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrders_CreatedDate",
                table: "MaintenanceOrders",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRunAssets_InspectionRunId_AssetId",
                table: "InspectionRunAssets",
                columns: new[] { "InspectionRunId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_CreatedAt",
                table: "InspectionOrders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_DueDate",
                table: "InspectionOrders",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ContractType",
                table: "Contracts",
                column: "ContractType");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNumberSequences_Prefix_Year",
                table: "OrderNumberSequences",
                columns: new[] { "Prefix", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderNumberSequences");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_CreatedDate",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceOrders_CreatedDate",
                table: "MaintenanceOrders");

            migrationBuilder.DropIndex(
                name: "IX_InspectionRunAssets_InspectionRunId_AssetId",
                table: "InspectionRunAssets");

            migrationBuilder.DropIndex(
                name: "IX_InspectionOrders_CreatedAt",
                table: "InspectionOrders");

            migrationBuilder.DropIndex(
                name: "IX_InspectionOrders_DueDate",
                table: "InspectionOrders");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_ContractType",
                table: "Contracts");
        }
    }
}
