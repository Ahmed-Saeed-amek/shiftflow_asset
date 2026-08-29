using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase33_OrderKindMaintenanceActionsDropNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "InspectionRunAssets");

            migrationBuilder.AddColumn<string>(
                name: "OrderKind",
                table: "InspectionOrders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Inspection");

            migrationBuilder.CreateTable(
                name: "MaintenanceActionTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceActionTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InspectionItemMaintenanceActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InspectionRunAssetId = table.Column<int>(type: "int", nullable: false),
                    MaintenanceActionTypeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionItemMaintenanceActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionItemMaintenanceActions_InspectionRunAssets_InspectionRunAssetId",
                        column: x => x.InspectionRunAssetId,
                        principalTable: "InspectionRunAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InspectionItemMaintenanceActions_MaintenanceActionTypes_MaintenanceActionTypeId",
                        column: x => x.MaintenanceActionTypeId,
                        principalTable: "MaintenanceActionTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionItemMaintenanceActions_InspectionRunAssetId_MaintenanceActionTypeId",
                table: "InspectionItemMaintenanceActions",
                columns: new[] { "InspectionRunAssetId", "MaintenanceActionTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionItemMaintenanceActions_MaintenanceActionTypeId",
                table: "InspectionItemMaintenanceActions",
                column: "MaintenanceActionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActionTypes_Name",
                table: "MaintenanceActionTypes",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionItemMaintenanceActions");

            migrationBuilder.DropTable(
                name: "MaintenanceActionTypes");

            migrationBuilder.DropColumn(
                name: "OrderKind",
                table: "InspectionOrders");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "InspectionRunAssets",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
