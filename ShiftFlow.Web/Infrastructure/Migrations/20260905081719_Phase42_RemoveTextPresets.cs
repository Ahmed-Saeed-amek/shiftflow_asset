using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase42_RemoveTextPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TextPresets");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "OrderTypes",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "#6c757d");

            // Backfill existing rows with distinct colors round-robin from the same 20-color
            // palette OrderTypeColors.Palette assigns new rows from, in SortOrder/Id order, so
            // pre-existing types get an immediate distinct identity too instead of all sharing
            // the "#6c757d" default above.
            migrationBuilder.Sql(@"
;WITH Ordered AS (
    SELECT Id, ROW_NUMBER() OVER (ORDER BY SortOrder, Id) - 1 AS RowNum FROM OrderTypes
),
Palette AS (
    SELECT * FROM (VALUES
        (0,N'#4C6EF5'),(1,N'#F76707'),(2,N'#2F9E44'),(3,N'#E03131'),(4,N'#9C36B5'),
        (5,N'#0C8599'),(6,N'#F08C00'),(7,N'#5C940D'),(8,N'#C2255C'),(9,N'#1971C2'),
        (10,N'#E8590C'),(11,N'#37B24D'),(12,N'#862E9C'),(13,N'#1098AD'),(14,N'#F59F00'),
        (15,N'#495057'),(16,N'#D6336C'),(17,N'#3B5BDB'),(18,N'#099268'),(19,N'#A61E4D')
    ) AS P(Idx, Hex)
)
UPDATE ot SET ot.Color = p.Hex
FROM OrderTypes ot
JOIN Ordered o ON o.Id = ot.Id
JOIN Palette p ON p.Idx = o.RowNum % 20;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "OrderTypes");

            migrationBuilder.CreateTable(
                name: "TextPresets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    OrderTypeId = table.Column<int>(type: "int", nullable: true),
                    FieldKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TextAr = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextPresets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TextPresets_AssetCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "AssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TextPresets_OrderTypes_OrderTypeId",
                        column: x => x.OrderTypeId,
                        principalTable: "OrderTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TextPresets_CategoryId",
                table: "TextPresets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_TextPresets_OrderTypeId",
                table: "TextPresets",
                column: "OrderTypeId");
        }
    }
}
