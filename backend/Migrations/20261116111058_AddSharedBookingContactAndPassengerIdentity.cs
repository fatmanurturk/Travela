using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedBookingContactAndPassengerIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentityNumber",
                table: "Users",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassengerIdentityNumberSnapshot",
                table: "Reservations",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "PurchaseGroups",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "PurchaseGroups",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdentityNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PassengerIdentityNumberSnapshot",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "PurchaseGroups");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "PurchaseGroups");
        }
    }
}
