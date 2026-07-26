using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase25_Inspections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InspectionRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftTaskId = table.Column<int>(type: "int", nullable: false),
                    ZoneId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionRuns_ShiftTasks_ShiftTaskId",
                        column: x => x.ShiftTaskId,
                        principalTable: "ShiftTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InspectionRuns_Zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "Zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InspectionRunAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InspectionRunId = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InspectedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    InspectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WorkOrderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionRunAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionRunAssets_AspNetUsers_InspectedByUserId",
                        column: x => x.InspectedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionRunAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionRunAssets_InspectionRuns_InspectionRunId",
                        column: x => x.InspectionRunId,
                        principalTable: "InspectionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InspectionRunAssets_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRunAssets_AssetId",
                table: "InspectionRunAssets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRunAssets_InspectedByUserId",
                table: "InspectionRunAssets",
                column: "InspectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRunAssets_InspectionRunId",
                table: "InspectionRunAssets",
                column: "InspectionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRunAssets_WorkOrderId",
                table: "InspectionRunAssets",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRuns_ShiftTaskId",
                table: "InspectionRuns",
                column: "ShiftTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRuns_ZoneId",
                table: "InspectionRuns",
                column: "ZoneId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionRunAssets");

            migrationBuilder.DropTable(
                name: "InspectionRuns");
        }
    }
}
