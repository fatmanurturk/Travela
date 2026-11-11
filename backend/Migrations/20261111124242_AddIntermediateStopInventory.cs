using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddIntermediateStopInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(IntermediateStopInventorySql.Preflight);

            migrationBuilder.DropIndex(
                name: "IX_PurchaseGroups_TripId",
                table: "PurchaseGroups");

            migrationBuilder.AddColumn<DateTime>(
                name: "AlightingArrivalSnapshot",
                table: "Reservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AlightingStopOrder",
                table: "Reservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BoardingDepartureSnapshot",
                table: "Reservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BoardingStopOrder",
                table: "Reservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AlightingStopOrder",
                table: "PurchaseGroups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BoardingStopOrder",
                table: "PurchaseGroups",
                type: "int",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Reservations_Id_TripId_SeatNumber",
                table: "Reservations",
                columns: new[] { "Id", "TripId", "SeatNumber" });

            migrationBuilder.CreateTable(
                name: "RouteStops",
                columns: table => new
                {
                    RouteId = table.Column<int>(type: "int", nullable: false),
                    StopOrder = table.Column<int>(type: "int", nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TerminalId = table.Column<int>(type: "int", nullable: true),
                    ArrivalOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    DepartureOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    DistanceFromStartKm = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteStops", x => new { x.RouteId, x.StopOrder });
                    table.CheckConstraint("CK_RouteStops_OrderTime", "[StopOrder] >= 1 AND [ArrivalOffsetMinutes] >= 0 AND [DepartureOffsetMinutes] >= [ArrivalOffsetMinutes] AND ([DistanceFromStartKm] IS NULL OR [DistanceFromStartKm] >= 0)");
                    table.ForeignKey(
                        name: "FK_RouteStops_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RouteStops_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TripStops",
                columns: table => new
                {
                    TripId = table.Column<int>(type: "int", nullable: false),
                    StopOrder = table.Column<int>(type: "int", nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TerminalId = table.Column<int>(type: "int", nullable: true),
                    TerminalName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    TerminalAddress = table.Column<string>(type: "nvarchar(640)", maxLength: 640, nullable: true),
                    BoardingInstructions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ArrivalTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DepartureTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DistanceFromStartKm = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripStops", x => new { x.TripId, x.StopOrder });
                    table.CheckConstraint("CK_TripStops_OrderTime", "[StopOrder] >= 1 AND [DepartureTime] >= [ArrivalTime] AND ([DistanceFromStartKm] IS NULL OR [DistanceFromStartKm] >= 0)");
                    table.ForeignKey(
                        name: "FK_TripStops_Terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "Terminals",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TripStops_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TripLegs",
                columns: table => new
                {
                    TripId = table.Column<int>(type: "int", nullable: false),
                    LegNumber = table.Column<int>(type: "int", nullable: false),
                    FromStopOrder = table.Column<int>(type: "int", nullable: false),
                    ToStopOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripLegs", x => new { x.TripId, x.LegNumber });
                    table.CheckConstraint("CK_TripLegs_Adjacent", "[LegNumber] = [FromStopOrder] AND [ToStopOrder] = [FromStopOrder] + 1");
                    table.ForeignKey(
                        name: "FK_TripLegs_TripStops_TripId_FromStopOrder",
                        columns: x => new { x.TripId, x.FromStopOrder },
                        principalTable: "TripStops",
                        principalColumns: new[] { "TripId", "StopOrder" });
                    table.ForeignKey(
                        name: "FK_TripLegs_TripStops_TripId_ToStopOrder",
                        columns: x => new { x.TripId, x.ToStopOrder },
                        principalTable: "TripStops",
                        principalColumns: new[] { "TripId", "StopOrder" });
                    table.ForeignKey(
                        name: "FK_TripLegs_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TripSegmentFares",
                columns: table => new
                {
                    TripId = table.Column<int>(type: "int", nullable: false),
                    FromStopOrder = table.Column<int>(type: "int", nullable: false),
                    ToStopOrder = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripSegmentFares", x => new { x.TripId, x.FromStopOrder, x.ToStopOrder });
                    table.CheckConstraint("CK_TripSegmentFares_Value", "[FromStopOrder] < [ToStopOrder] AND [Amount] >= 0.01 AND [Source] IN (N'FullRoute', N'Manual', N'Automatic') AND ([IsApproved] = 0 OR [ApprovedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TripSegmentFares_TripStops_TripId_FromStopOrder",
                        columns: x => new { x.TripId, x.FromStopOrder },
                        principalTable: "TripStops",
                        principalColumns: new[] { "TripId", "StopOrder" });
                    table.ForeignKey(
                        name: "FK_TripSegmentFares_TripStops_TripId_ToStopOrder",
                        columns: x => new { x.TripId, x.ToStopOrder },
                        principalTable: "TripStops",
                        principalColumns: new[] { "TripId", "StopOrder" });
                    table.ForeignKey(
                        name: "FK_TripSegmentFares_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReservationLegs",
                columns: table => new
                {
                    ReservationId = table.Column<int>(type: "int", nullable: false),
                    LegNumber = table.Column<int>(type: "int", nullable: false),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    SeatNumber = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationLegs", x => new { x.ReservationId, x.LegNumber });
                    table.ForeignKey(
                        name: "FK_ReservationLegs_Reservations_ReservationId_TripId_SeatNumber",
                        columns: x => new { x.ReservationId, x.TripId, x.SeatNumber },
                        principalTable: "Reservations",
                        principalColumns: new[] { "Id", "TripId", "SeatNumber" });
                    table.ForeignKey(
                        name: "FK_ReservationLegs_TripLegs_TripId_LegNumber",
                        columns: x => new { x.TripId, x.LegNumber },
                        principalTable: "TripLegs",
                        principalColumns: new[] { "TripId", "LegNumber" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId_AlightingStopOrder",
                table: "Reservations",
                columns: new[] { "TripId", "AlightingStopOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TripId_BoardingStopOrder",
                table: "Reservations",
                columns: new[] { "TripId", "BoardingStopOrder" });



            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_Segment",
                table: "Reservations",
                sql: "([BoardingStopOrder] IS NULL AND [AlightingStopOrder] IS NULL AND [BoardingDepartureSnapshot] IS NULL AND [AlightingArrivalSnapshot] IS NULL) OR ([BoardingStopOrder] IS NOT NULL AND [AlightingStopOrder] IS NOT NULL AND [BoardingStopOrder] < [AlightingStopOrder] AND [BoardingDepartureSnapshot] IS NOT NULL AND [AlightingArrivalSnapshot] > [BoardingDepartureSnapshot])");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGroups_TripId_AlightingStopOrder",
                table: "PurchaseGroups",
                columns: new[] { "TripId", "AlightingStopOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGroups_TripId_BoardingStopOrder",
                table: "PurchaseGroups",
                columns: new[] { "TripId", "BoardingStopOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationLegs_ReservationId_TripId_SeatNumber",
                table: "ReservationLegs",
                columns: new[] { "ReservationId", "TripId", "SeatNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationLegs_TripId_LegNumber",
                table: "ReservationLegs",
                columns: new[] { "TripId", "LegNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationLegs_TripId_SeatNumber_LegNumber",
                table: "ReservationLegs",
                columns: new[] { "TripId", "SeatNumber", "LegNumber" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_RouteId_City",
                table: "RouteStops",
                columns: new[] { "RouteId", "City" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_TerminalId",
                table: "RouteStops",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_TripLegs_TripId_FromStopOrder",
                table: "TripLegs",
                columns: new[] { "TripId", "FromStopOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TripLegs_TripId_ToStopOrder",
                table: "TripLegs",
                columns: new[] { "TripId", "ToStopOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TripSegmentFares_TripId_ToStopOrder",
                table: "TripSegmentFares",
                columns: new[] { "TripId", "ToStopOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_TerminalId",
                table: "TripStops",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_TripId_City",
                table: "TripStops",
                columns: new[] { "TripId", "City" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseGroups_TripStops_TripId_AlightingStopOrder",
                table: "PurchaseGroups",
                columns: new[] { "TripId", "AlightingStopOrder" },
                principalTable: "TripStops",
                principalColumns: new[] { "TripId", "StopOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseGroups_TripStops_TripId_BoardingStopOrder",
                table: "PurchaseGroups",
                columns: new[] { "TripId", "BoardingStopOrder" },
                principalTable: "TripStops",
                principalColumns: new[] { "TripId", "StopOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_TripStops_TripId_AlightingStopOrder",
                table: "Reservations",
                columns: new[] { "TripId", "AlightingStopOrder" },
                principalTable: "TripStops",
                principalColumns: new[] { "TripId", "StopOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_TripStops_TripId_BoardingStopOrder",
                table: "Reservations",
                columns: new[] { "TripId", "BoardingStopOrder" },
                principalTable: "TripStops",
                principalColumns: new[] { "TripId", "StopOrder" });
            migrationBuilder.Sql(IntermediateStopInventorySql.Backfill);
            migrationBuilder.Sql(IntermediateStopInventorySql.Legs);
            migrationBuilder.Sql(IntermediateStopInventorySql.Reservations);
            migrationBuilder.Sql(IntermediateStopInventorySql.Stops);
            migrationBuilder.Sql(IntermediateStopInventorySql.TripLegs);
            migrationBuilder.Sql(IntermediateStopInventorySql.Groups);
            migrationBuilder.Sql(IntermediateStopInventorySql.Orders);
            // The populated, validated leg unique index already protects every active reservation.
            migrationBuilder.DropIndex(name: "IX_Reservations_TripId_SeatNumber", table: "Reservations");
            migrationBuilder.CreateIndex(name: "IX_Reservations_TripId_SeatNumber", table: "Reservations", columns: new[] { "TripId", "SeatNumber" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("THROW 51013, N'Ara durak geçişi otomatik geri alınamaz. Yazmaları durdurun ve doğrulanmış geçiş öncesi yedekten geri dönün.', 1;");
        }
    }
}