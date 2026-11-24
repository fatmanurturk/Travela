using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class ExtendStaffProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BaggageExperience",
                table: "CompanyStaff",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BaggageExperienceNotes",
                table: "CompanyStaff",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CareerStartYear",
                table: "CompanyStaff",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactName",
                table: "CompanyStaff",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactPhone",
                table: "CompanyStaff",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactRelationship",
                table: "CompanyStaff",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EmploymentStartDate",
                table: "CompanyStaff",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Languages",
                table: "CompanyStaff",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'[]'");

            migrationBuilder.AddColumn<string>(
                name: "LicenseClasses",
                table: "CompanyStaff",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'[]'");

            migrationBuilder.AddColumn<bool>(
                name: "PassengerCommunicationExperience",
                table: "CompanyStaff",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PassengerCommunicationExperienceNotes",
                table: "CompanyStaff",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ServiceExperience",
                table: "CompanyStaff",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ServiceExperienceNotes",
                table: "CompanyStaff",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleTypes",
                table: "CompanyStaff",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'[]'");

            migrationBuilder.CreateTable(
                name: "StaffDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StaffId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsIndefinite = table.Column<bool>(type: "bit", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Size = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffDocuments", x => x.Id);
                    table.CheckConstraint("CK_StaffDocuments_Size", "[Size] > 0 AND [Size] <= 10485760 AND DATALENGTH([Data]) = [Size]");
                    table.CheckConstraint("CK_StaffDocuments_Validity", "([IsIndefinite] = 1 AND [ExpiresOn] IS NULL) OR ([IsIndefinite] = 0 AND [ExpiresOn] IS NOT NULL AND [ExpiresOn] >= [IssuedOn])");
                    table.ForeignKey(
                        name: "FK_StaffDocuments_CompanyStaff_StaffId_CompanyId",
                        columns: x => new { x.StaffId, x.CompanyId },
                        principalTable: "CompanyStaff",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StaffPhotos",
                columns: table => new
                {
                    StaffId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffPhotos", x => x.StaffId);
                    table.CheckConstraint("CK_StaffPhotos_Size", "DATALENGTH([Data]) > 0 AND DATALENGTH([Data]) <= 5242880");
                    table.ForeignKey(
                        name: "FK_StaffPhotos_CompanyStaff_StaffId_CompanyId",
                        columns: x => new { x.StaffId, x.CompanyId },
                        principalTable: "CompanyStaff",
                        principalColumns: new[] { "Id", "CompanyId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffDocuments_CompanyId_ExpiresOn",
                table: "StaffDocuments",
                columns: new[] { "CompanyId", "ExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffDocuments_StaffId_CompanyId",
                table: "StaffDocuments",
                columns: new[] { "StaffId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffPhotos_StaffId_CompanyId",
                table: "StaffPhotos",
                columns: new[] { "StaffId", "CompanyId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaffDocuments");

            migrationBuilder.DropTable(
                name: "StaffPhotos");

            migrationBuilder.DropColumn(
                name: "BaggageExperience",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "BaggageExperienceNotes",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "CareerStartYear",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "EmergencyContactName",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "EmergencyContactPhone",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "EmergencyContactRelationship",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "EmploymentStartDate",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "Languages",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "LicenseClasses",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "PassengerCommunicationExperience",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "PassengerCommunicationExperienceNotes",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "ServiceExperience",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "ServiceExperienceNotes",
                table: "CompanyStaff");

            migrationBuilder.DropColumn(
                name: "VehicleTypes",
                table: "CompanyStaff");
        }
    }
}
