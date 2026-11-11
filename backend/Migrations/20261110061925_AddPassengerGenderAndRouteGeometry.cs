using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPassengerGenderAndRouteGeometry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeometryJson",
                table: "Routes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassengerGender",
                table: "Reservations",
                type: "nvarchar(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_PassengerGender",
                table: "Reservations",
                sql: "[PassengerGender] IS NULL OR [PassengerGender] IN (N'Female', N'Male')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [Reservations] WHERE [PassengerGender] IS NOT NULL) OR EXISTS (SELECT 1 FROM [Routes] WHERE [GeometryJson] IS NOT NULL) THROW 51000, N'Cinsiyet veya yol geometrisi verisi varken geri migration veri kaybına yol açar; işlem engellendi.', 1;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_Reservations_PassengerGender",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "GeometryJson",
                table: "Routes");

            migrationBuilder.DropColumn(
                name: "PassengerGender",
                table: "Reservations");
        }
    }
}
