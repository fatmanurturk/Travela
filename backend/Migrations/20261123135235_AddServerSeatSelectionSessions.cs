using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddServerSeatSelectionSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SelectionSessionId",
                table: "Reservations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SeatSelectionSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IpHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    BoardingStopOrder = table.Column<int>(type: "int", nullable: false),
                    AlightingStopOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PurchaseGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatSelectionSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeatSelectionSessions_PurchaseGroups_PurchaseGroupId",
                        column: x => x.PurchaseGroupId,
                        principalTable: "PurchaseGroups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SeatSelectionSessions_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_SelectionSessionId_Status_HoldExpiresAt",
                table: "Reservations",
                columns: new[] { "SelectionSessionId", "Status", "HoldExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SeatSelectionSessions_IpHash_ExpiresAt",
                table: "SeatSelectionSessions",
                columns: new[] { "IpHash", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SeatSelectionSessions_PurchaseGroupId",
                table: "SeatSelectionSessions",
                column: "PurchaseGroupId",
                unique: true,
                filter: "[PurchaseGroupId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SeatSelectionSessions_TripId",
                table: "SeatSelectionSessions",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_SeatSelectionSessions_UserId_ExpiresAt",
                table: "SeatSelectionSessions",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_SeatSelectionSessions_SelectionSessionId",
                table: "Reservations",
                column: "SelectionSessionId",
                principalTable: "SeatSelectionSessions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM SeatSelectionSessions) THROW 51032, N'Koltuk ayırma oturumları varken bu geçiş geri alınamaz. Mevcut siparişleri koruyan ileri geçiş uygulayın.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_SeatSelectionSessions_SelectionSessionId",
                table: "Reservations");

            migrationBuilder.DropTable(
                name: "SeatSelectionSessions");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_SelectionSessionId_Status_HoldExpiresAt",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "SelectionSessionId",
                table: "Reservations");
        }
    }
}
