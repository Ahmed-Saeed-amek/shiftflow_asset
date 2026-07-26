using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_AssetTransferHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetTransferHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    FromLocationId = table.Column<int>(type: "int", nullable: true),
                    ToLocationId = table.Column<int>(type: "int", nullable: true),
                    FromResponsibleUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ToResponsibleUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    TransferDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerformedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTransferHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetTransferHistories_AspNetUsers_FromResponsibleUserId",
                        column: x => x.FromResponsibleUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetTransferHistories_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetTransferHistories_AspNetUsers_ToResponsibleUserId",
                        column: x => x.ToResponsibleUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetTransferHistories_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetTransferHistories_Locations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AssetTransferHistories_Locations_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransferHistories_AssetId_TransferDate",
                table: "AssetTransferHistories",
                columns: new[] { "AssetId", "TransferDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransferHistories_FromLocationId",
                table: "AssetTransferHistories",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransferHistories_FromResponsibleUserId",
                table: "AssetTransferHistories",
                column: "FromResponsibleUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransferHistories_PerformedByUserId",
                table: "AssetTransferHistories",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransferHistories_ToLocationId",
                table: "AssetTransferHistories",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTransferHistories_ToResponsibleUserId",
                table: "AssetTransferHistories",
                column: "ToResponsibleUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetTransferHistories");
        }
    }
}
