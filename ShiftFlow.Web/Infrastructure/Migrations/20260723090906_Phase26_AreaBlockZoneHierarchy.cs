using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase26_AreaBlockZoneHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Zones_Areas_AreaId",
                table: "Zones");

            migrationBuilder.RenameColumn(
                name: "AreaId",
                table: "Zones",
                newName: "BlockId");

            migrationBuilder.RenameIndex(
                name: "IX_Zones_AreaId_Name",
                table: "Zones",
                newName: "IX_Zones_BlockId_Name");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Zones",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Blocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<int>(type: "int", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Blocks_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_AreaId_Number",
                table: "Blocks",
                columns: new[] { "AreaId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Zones_Blocks_BlockId",
                table: "Zones",
                column: "BlockId",
                principalTable: "Blocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Zones_Blocks_BlockId",
                table: "Zones");

            migrationBuilder.DropTable(
                name: "Blocks");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Zones");

            migrationBuilder.RenameColumn(
                name: "BlockId",
                table: "Zones",
                newName: "AreaId");

            migrationBuilder.RenameIndex(
                name: "IX_Zones_BlockId_Name",
                table: "Zones",
                newName: "IX_Zones_AreaId_Name");

            migrationBuilder.AddForeignKey(
                name: "FK_Zones_Areas_AreaId",
                table: "Zones",
                column: "AreaId",
                principalTable: "Areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
