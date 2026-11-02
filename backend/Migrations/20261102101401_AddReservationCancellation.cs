using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_TripId_SeatNumber",
                table: "Reservations");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Reservations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Reservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Reservations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            // Preserve old reservations; reconcile only those already attached to cancelled trips.
            migrationBuilder.Sql(@"
                UPDATE t SET AvailableSeats = t.AvailableSeats + x.ReservationCount
                FROM Trips t
                INNER JOIN (SELECT TripId, COUNT(*) AS ReservationCount FROM Reservations GROUP BY TripId) x ON x.TripId = t.Id
                WHERE LOWER(t.Status) IN (N'cancelled', N'canceled', N'iptal', N'iptal edildi');
                UPDATE r SET Status = N'Cancelled', CancelledAt = COALESCE(t.CancelledAt, SYSUTCDATETIME()),
                    CancellationReason = N'Sefer iptal edildi: ' + COALESCE(t.CancellationReason, N'Firma tarafından iptal edildi.')
                FROM Reservations r INNER JOIN Trips t ON t.Id = r.TripId
                WHERE LOWER(t.Status) IN (N'cancelled', N'canceled', N'iptal', N'iptal edildi');
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId_SeatNumber",
                table: "Reservations",
                columns: new[] { "TripId", "SeatNumber" },
                unique: true,
                filter: "[Status] = N'Active'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_Status",
                table: "Reservations",
                sql: "([Status] = N'Active' AND [CancelledAt] IS NULL) OR ([Status] = N'Cancelled' AND [CancelledAt] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cancelled history may share seats with a later active booking. Refuse lossy rollback.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM Reservations WHERE Status = N'Cancelled') THROW 51000, 'Iptal gecmisi varken bu migration veri kaybi olmadan geri alinamaz.', 1;");
            migrationBuilder.DropIndex(
                name: "IX_Reservations_TripId_SeatNumber",
                table: "Reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reservations_Status",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Reservations");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId_SeatNumber",
                table: "Reservations",
                columns: new[] { "TripId", "SeatNumber" },
                unique: true);
        }
    }
}
