using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTravelNotificationsAndTripChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailNotificationsEnabled",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmsNotificationsEnabled",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ChangeVersion",
                table: "Trips",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChangeCancellationUntil",
                table: "Reservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TicketJourneyJson",
                table: "Reservations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TravelNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<int>(type: "int", nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Polls = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TravelNotifications_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TripChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    AffectedTickets = table.Column<int>(type: "int", nullable: false),
                    ReleasedHolds = table.Column<int>(type: "int", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TripChanges_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "NotificationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationAttempts_TravelNotifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "TravelNotifications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TripChangeTickets",
                columns: table => new
                {
                    TripChangeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripChangeTickets", x => new { x.TripChangeId, x.ReservationId });
                    table.ForeignKey(
                        name: "FK_TripChangeTickets_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TripChangeTickets_TripChanges_TripChangeId",
                        column: x => x.TripChangeId,
                        principalTable: "TripChanges",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationAttempts_NotificationId_CreatedAt",
                table: "NotificationAttempts",
                columns: new[] { "NotificationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TravelNotifications_ReservationId_EventKey_Channel",
                table: "TravelNotifications",
                columns: new[] { "ReservationId", "EventKey", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TravelNotifications_Status_NextAttemptAt",
                table: "TravelNotifications",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TripChanges_RequestKey",
                table: "TripChanges",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripChanges_TripId_Version",
                table: "TripChanges",
                columns: new[] { "TripId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripChangeTickets_ReservationId",
                table: "TripChangeTickets",
                column: "ReservationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [TravelNotifications]) OR EXISTS (SELECT 1 FROM [TripChanges])
                    OR EXISTS (SELECT 1 FROM [Users] WHERE [EmailNotificationsEnabled] = 0 OR [SmsNotificationsEnabled] = 1)
                    OR EXISTS (SELECT 1 FROM [Reservations] WHERE [TicketJourneyJson] IS NOT NULL OR [ChangeCancellationUntil] IS NOT NULL)
                    THROW 51000, N'Bildirim, değişiklik geçmişi veya bilet yolculuk bilgileri kaybedileceği için geri migration engellendi.', 1;
                """);
            migrationBuilder.DropTable(
                name: "NotificationAttempts");

            migrationBuilder.DropTable(
                name: "TripChangeTickets");

            migrationBuilder.DropTable(
                name: "TravelNotifications");

            migrationBuilder.DropTable(
                name: "TripChanges");

            migrationBuilder.DropColumn(
                name: "EmailNotificationsEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SmsNotificationsEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ChangeVersion",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ChangeCancellationUntil",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "TicketJourneyJson",
                table: "Reservations");
        }
    }
}
