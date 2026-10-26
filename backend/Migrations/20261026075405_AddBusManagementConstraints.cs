using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddBusManagementConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PlateNumber",
                table: "Buses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedPlateNumber",
                table: "Buses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [Buses] SET [NormalizedPlateNumber] = UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([PlateNumber])), N' ', N''), N'-', N''), CHAR(9), N''), CHAR(10), N''), CHAR(13), N''));");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedPlateNumber",
                table: "Buses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Buses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Buses_NormalizedPlateNumber",
                table: "Buses",
                column: "NormalizedPlateNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Buses_NormalizedPlateNumber",
                table: "Buses");

            migrationBuilder.DropColumn(
                name: "NormalizedPlateNumber",
                table: "Buses");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Buses");

            migrationBuilder.AlterColumn<string>(
                name: "PlateNumber",
                table: "Buses",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);
        }
    }
}
