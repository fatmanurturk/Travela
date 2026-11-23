using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyStaffAndTripCrew : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyStaff",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Duty = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyStaff", x => x.Id);
                    table.UniqueConstraint("AK_CompanyStaff_Id_CompanyId", x => new { x.Id, x.CompanyId });
                    table.CheckConstraint("CK_CompanyStaff_Duty", "[Duty] IN ('Driver','Attendant')");
                    table.ForeignKey(
                        name: "FK_CompanyStaff_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TripCrews",
                columns: table => new
                {
                    TripId = table.Column<int>(type: "int", nullable: false),
                    StaffId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Duty = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripCrews", x => new { x.TripId, x.StaffId });
                    table.CheckConstraint("CK_TripCrews_Duty", "[Duty] IN ('Driver','Attendant')");
                    table.ForeignKey(
                        name: "FK_TripCrews_CompanyStaff_StaffId_CompanyId",
                        columns: x => new { x.StaffId, x.CompanyId },
                        principalTable: "CompanyStaff",
                        principalColumns: new[] { "Id", "CompanyId" });
                    table.ForeignKey(
                        name: "FK_TripCrews_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyStaff_CompanyId_IsActive_Duty",
                table: "CompanyStaff",
                columns: new[] { "CompanyId", "IsActive", "Duty" });

            migrationBuilder.CreateIndex(
                name: "IX_TripCrews_StaffId_CompanyId",
                table: "TripCrews",
                columns: new[] { "StaffId", "CompanyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripCrews");

            migrationBuilder.DropTable(
                name: "CompanyStaff");
        }
    }
}
