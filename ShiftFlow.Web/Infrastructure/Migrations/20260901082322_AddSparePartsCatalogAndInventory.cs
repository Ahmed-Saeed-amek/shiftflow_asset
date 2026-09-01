using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSparePartsCatalogAndInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SparePartId",
                table: "WorkOrderParts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostAtUsage",
                table: "WorkOrderParts",
                type: "decimal(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SparePartId",
                table: "MaintenanceOrderParts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCostAtUsage",
                table: "MaintenanceOrderParts",
                type: "decimal(12,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SpareParts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Sku = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitCost = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                    StockQuantity = table.Column<int>(type: "int", nullable: false),
                    ReorderThreshold = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpareParts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SparePartAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SparePartId = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SparePartAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SparePartAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SparePartAssets_SpareParts_SparePartId",
                        column: x => x.SparePartId,
                        principalTable: "SpareParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderParts_SparePartId",
                table: "WorkOrderParts",
                column: "SparePartId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrderParts_SparePartId",
                table: "MaintenanceOrderParts",
                column: "SparePartId");

            migrationBuilder.CreateIndex(
                name: "IX_SparePartAssets_AssetId",
                table: "SparePartAssets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_SparePartAssets_SparePartId_AssetId",
                table: "SparePartAssets",
                columns: new[] { "SparePartId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpareParts_Name",
                table: "SpareParts",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceOrderParts_SpareParts_SparePartId",
                table: "MaintenanceOrderParts",
                column: "SparePartId",
                principalTable: "SpareParts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrderParts_SpareParts_SparePartId",
                table: "WorkOrderParts",
                column: "SparePartId",
                principalTable: "SpareParts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceOrderParts_SpareParts_SparePartId",
                table: "MaintenanceOrderParts");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrderParts_SpareParts_SparePartId",
                table: "WorkOrderParts");

            migrationBuilder.DropTable(
                name: "SparePartAssets");

            migrationBuilder.DropTable(
                name: "SpareParts");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrderParts_SparePartId",
                table: "WorkOrderParts");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceOrderParts_SparePartId",
                table: "MaintenanceOrderParts");

            migrationBuilder.DropColumn(
                name: "SparePartId",
                table: "WorkOrderParts");

            migrationBuilder.DropColumn(
                name: "UnitCostAtUsage",
                table: "WorkOrderParts");

            migrationBuilder.DropColumn(
                name: "SparePartId",
                table: "MaintenanceOrderParts");

            migrationBuilder.DropColumn(
                name: "UnitCostAtUsage",
                table: "MaintenanceOrderParts");
        }
    }
}
