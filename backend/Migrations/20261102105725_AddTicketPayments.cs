using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HoldExpiresAt",
                table: "Reservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Reservations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.AddColumn<string>(
                name: "Pnr",
                table: "Reservations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LocalPaymentRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RefundOutcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RefundKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RefundTransfers = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalPaymentRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<int>(type: "int", nullable: false),
                    PassengerId = table.Column<int>(type: "int", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsTest = table.Column<bool>(type: "bit", nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProviderReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RefundReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefundAttempts = table.Column<int>(type: "int", nullable: false),
                    NextRefundAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundLeaseUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundLeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NeedsAttention = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentOrders_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Pnr",
                table: "Reservations",
                column: "Pnr",
                unique: true,
                filter: "[Pnr] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentNotifications_Provider_EventId",
                table: "PaymentNotifications",
                columns: new[] { "Provider", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_PassengerId_RequestKey",
                table: "PaymentOrders",
                columns: new[] { "PassengerId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_RefundKey",
                table: "PaymentOrders",
                column: "RefundKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_RefundStatus_NextRefundAttemptAt",
                table: "PaymentOrders",
                columns: new[] { "RefundStatus", "NextRefundAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_ReservationId",
                table: "PaymentOrders",
                column: "ReservationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM PaymentOrders) OR EXISTS (SELECT 1 FROM Reservations WHERE Kind <> N'Legacy') THROW 51000, 'Odeme ve bilet gecmisi varken veri kayipli geri migration engellendi.', 1;");
            migrationBuilder.DropTable(
                name: "LocalPaymentRecords");

            migrationBuilder.DropTable(
                name: "PaymentNotifications");

            migrationBuilder.DropTable(
                name: "PaymentOrders");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_Pnr",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "HoldExpiresAt",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Pnr",
                table: "Reservations");
        }
    }
}
