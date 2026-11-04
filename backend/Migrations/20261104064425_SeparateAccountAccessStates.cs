using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class SeparateAccountAccessStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ActivationPending",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PasswordResetRequired",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Users] WHERE [ActivationPending] = 1 OR [PasswordResetRequired] = 1)
                    THROW 51000, N'Bekleyen aktivasyon veya şifre sıfırlama durumları kaybedileceği için geri migration engellendi.', 1;
                """);
            migrationBuilder.DropColumn(
                name: "ActivationPending",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetRequired",
                table: "Users");
        }
    }
}
