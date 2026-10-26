using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTripCancellationAndSchedulingMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_BusId",
                table: "Trips");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Trips",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Trips",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledByUserId",
                table: "Trips",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Trips",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_BusId_DepartureTime",
                table: "Trips",
                columns: new[] { "BusId", "DepartureTime" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_CancelledByUserId",
                table: "Trips",
                column: "CancelledByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_Users_CancelledByUserId",
                table: "Trips",
                column: "CancelledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trips_Users_CancelledByUserId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_BusId_DepartureTime",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_CancelledByUserId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Trips");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_BusId",
                table: "Trips",
                column: "BusId");
        }
    }
}
