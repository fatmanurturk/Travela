using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketPassengerSnapshotsAndPaymentHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PassengerCapturedAt",
                table: "Reservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassengerEmailSnapshot",
                table: "Reservations",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassengerNameSnapshot",
                table: "Reservations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassengerPhoneSnapshot",
                table: "Reservations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassengerSnapshotSource",
                table: "Reservations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReconciledAt",
                table: "PaymentOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextReconciliationAt",
                table: "PaymentOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReconciliationAttempts",
                table: "PaymentOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RefundStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentEvents_PaymentOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_PaymentStatus_NextReconciliationAt",
                table: "PaymentOrders",
                columns: new[] { "PaymentStatus", "NextReconciliationAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_ActorUserId_RequestKey",
                table: "PaymentEvents",
                columns: new[] { "ActorUserId", "RequestKey" },
                unique: true,
                filter: "[RequestKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_OrderId_CreatedAt",
                table: "PaymentEvents",
                columns: new[] { "OrderId", "CreatedAt" });
            migrationBuilder.Sql("""
                UPDATE r SET [PassengerNameSnapshot] = u.[FullName], [PassengerEmailSnapshot] = u.[Email],
                    [PassengerPhoneSnapshot] = u.[PhoneNumber], [PassengerCapturedAt] = SYSUTCDATETIME(), [PassengerSnapshotSource] = N'Migration'
                FROM [Reservations] r INNER JOIN [Users] u ON r.[PassengerId] = u.[Id];
                INSERT INTO [PaymentEvents] ([OrderId], [Type], [CreatedAt], [PaymentStatus], [RefundStatus], [RefundAmount], [Reason])
                SELECT [Id], N'ImportedState', SYSUTCDATETIME(), [PaymentStatus], [RefundStatus], [RefundAmount],
                    N'Geçiş sırasında mevcut durum kaydedildi. Önceki hareket geçmişi bulunmuyor.' FROM [PaymentOrders];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [PaymentEvents]) OR EXISTS (SELECT 1 FROM [Reservations] WHERE [PassengerCapturedAt] IS NOT NULL)
                    THROW 51000, N'Sabit yolcu bilgileri veya ödeme işlem geçmişi kaybedileceği için geri migration engellendi.', 1;
                """);
            migrationBuilder.DropTable(
                name: "PaymentEvents");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_PaymentStatus_NextReconciliationAt",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "PassengerCapturedAt",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "PassengerEmailSnapshot",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "PassengerNameSnapshot",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "PassengerPhoneSnapshot",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "PassengerSnapshotSource",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "LastReconciledAt",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "NextReconciliationAt",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "ReconciliationAttempts",
                table: "PaymentOrders");
        }
    }
}
