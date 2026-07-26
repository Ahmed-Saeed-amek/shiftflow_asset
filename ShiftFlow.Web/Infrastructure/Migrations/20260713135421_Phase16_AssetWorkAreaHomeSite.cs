using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase16_AssetWorkAreaHomeSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_Locations_LocationId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Criticality",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Distributor",
                table: "Assets");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "Assets",
                newName: "WorkAreaId");

            migrationBuilder.RenameIndex(
                name: "IX_Assets_LocationId",
                table: "Assets",
                newName: "IX_Assets_WorkAreaId");

            // The rename above preserves the old Location IDs in the now-renamed
            // WorkAreaId column — those aren't valid WorkArea IDs, so they'd
            // violate the FK added below. Backfill every asset onto the first
            // WorkArea (same "first row" convention DbSeeder used for LocationId).
            migrationBuilder.Sql("UPDATE Assets SET WorkAreaId = (SELECT TOP 1 Id FROM WorkAreas ORDER BY Id)");

            migrationBuilder.AddColumn<int>(
                name: "VendorId",
                table: "Assets",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_VendorId",
                table: "Assets",
                column: "VendorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Vendors_VendorId",
                table: "Assets",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_WorkAreas_WorkAreaId",
                table: "Assets",
                column: "WorkAreaId",
                principalTable: "WorkAreas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_Vendors_VendorId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_Assets_WorkAreas_WorkAreaId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_VendorId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "Assets");

            migrationBuilder.RenameColumn(
                name: "WorkAreaId",
                table: "Assets",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_Assets_WorkAreaId",
                table: "Assets",
                newName: "IX_Assets_LocationId");

            migrationBuilder.AddColumn<string>(
                name: "Criticality",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Distributor",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Locations_LocationId",
                table: "Assets",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
