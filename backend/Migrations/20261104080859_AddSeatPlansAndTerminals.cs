using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatPlansAndTerminals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArrivalTerminalAddress",
                table: "Trips",
                type: "nvarchar(640)",
                maxLength: 640,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ArrivalTerminalId",
                table: "Trips",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArrivalTerminalName",
                table: "Trips",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoardingInstructions",
                table: "Trips",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepartureTerminalAddress",
                table: "Trips",
                type: "nvarchar(640)",
                maxLength: 640,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepartureTerminalId",
                table: "Trips",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepartureTerminalName",
                table: "Trips",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeatCapacitySnapshot",
                table: "Trips",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeatLayout",
                table: "Trips",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unspecified");

            migrationBuilder.AddColumn<string>(
                name: "SeatPlanJson",
                table: "Trips",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SellableSeatCount",
                table: "Trips",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeatLayout",
                table: "Buses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unspecified");

            migrationBuilder.AddColumn<string>(
                name: "SeatPlanJson",
                table: "Buses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Terminals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terminals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_ArrivalTerminalId",
                table: "Trips",
                column: "ArrivalTerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DepartureTerminalId",
                table: "Trips",
                column: "DepartureTerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_Terminals_City_Name",
                table: "Terminals",
                columns: new[] { "City", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_Terminals_ArrivalTerminalId",
                table: "Trips",
                column: "ArrivalTerminalId",
                principalTable: "Terminals",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_Terminals_DepartureTerminalId",
                table: "Trips",
                column: "DepartureTerminalId",
                principalTable: "Terminals",
                principalColumn: "Id");
            migrationBuilder.Sql("""
                UPDATE t SET [SeatCapacitySnapshot] = b.[SeatCapacity], [SellableSeatCount] = b.[SeatCapacity]
                FROM [Trips] t INNER JOIN [Buses] b ON b.[Id] = t.[BusId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Trips] WHERE [SeatPlanJson] IS NOT NULL OR [DepartureTerminalId] IS NOT NULL OR [ArrivalTerminalId] IS NOT NULL)
                    OR EXISTS (SELECT 1 FROM [Buses] WHERE [SeatPlanJson] IS NOT NULL) OR EXISTS (SELECT 1 FROM [Terminals])
                    THROW 51000, N'Koltuk planları veya terminal bilgileri kaybedileceği için geri migration engellendi.', 1;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Trips_Terminals_ArrivalTerminalId",
                table: "Trips");

            migrationBuilder.DropForeignKey(
                name: "FK_Trips_Terminals_DepartureTerminalId",
                table: "Trips");

            migrationBuilder.DropTable(
                name: "Terminals");

            migrationBuilder.DropIndex(
                name: "IX_Trips_ArrivalTerminalId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_DepartureTerminalId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ArrivalTerminalAddress",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ArrivalTerminalId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ArrivalTerminalName",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "BoardingInstructions",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DepartureTerminalAddress",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DepartureTerminalId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DepartureTerminalName",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "SeatCapacitySnapshot",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "SeatLayout",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "SeatPlanJson",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "SellableSeatCount",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "SeatLayout",
                table: "Buses");

            migrationBuilder.DropColumn(
                name: "SeatPlanJson",
                table: "Buses");
        }
    }
}
