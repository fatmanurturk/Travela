using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingDatesAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_CompanyId",
                table: "Trips");

            migrationBuilder.AddColumn<DateTime>(
                name: "IssuedAt",
                table: "Reservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundRequestedAt",
                table: "PaymentOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundStatusChangedAt",
                table: "PaymentOrders",
                type: "datetime2",
                nullable: true);

            // Older issuance and payment verification were committed together. Preserve
            // creation/cancellation history; unknown pending/failed refund dates stay NULL.
            migrationBuilder.Sql("UPDATE r SET IssuedAt = p.PaidAt FROM Reservations r JOIN PaymentOrders p ON p.ReservationId = r.Id WHERE r.Kind = N'Ticket' AND r.Pnr IS NOT NULL AND p.PaymentStatus = N'Paid'; UPDATE PaymentOrders SET RefundStatusChangedAt = RefundedAt WHERE RefundStatus = N'Succeeded';");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_CompanyId_DepartureTime",
                table: "Trips",
                columns: new[] { "CompanyId", "DepartureTime" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_IssuedAt",
                table: "Reservations",
                column: "IssuedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId_Kind_Status",
                table: "Reservations",
                columns: new[] { "TripId", "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_IsTest_PaidAt",
                table: "PaymentOrders",
                columns: new[] { "IsTest", "PaidAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_IsTest_RefundedAt",
                table: "PaymentOrders",
                columns: new[] { "IsTest", "RefundedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_IsTest_RefundStatusChangedAt",
                table: "PaymentOrders",
                columns: new[] { "IsTest", "RefundStatusChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_CompanyId_DepartureTime",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_IssuedAt",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_TripId_Kind_Status",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_IsTest_PaidAt",
                table: "PaymentOrders");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_IsTest_RefundedAt",
                table: "PaymentOrders");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_IsTest_RefundStatusChangedAt",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "IssuedAt",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "RefundRequestedAt",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "RefundStatusChangedAt",
                table: "PaymentOrders");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_CompanyId",
                table: "Trips",
                column: "CompanyId");
        }
    }
}
