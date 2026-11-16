using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class EnableIntermediateStopSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TripStops_TripId_City",
                table: "TripStops");

            migrationBuilder.DropIndex(
                name: "IX_RouteStops_RouteId_City",
                table: "RouteStops");

            migrationBuilder.AddColumn<bool>(
                name: "CanAlight",
                table: "TripStops",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanBoard",
                table: "TripStops",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CityId",
                table: "TripStops",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanAlight",
                table: "RouteStops",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanBoard",
                table: "RouteStops",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CityId",
                table: "RouteStops",
                type: "int",
                nullable: true);

            // Preserve existing endpoint sales. Intermediate permissions require explicit operator input.
            migrationBuilder.Sql("""
UPDATE TripStops SET CityId = CASE City COLLATE Turkish_100_CI_AS WHEN N'Adana' THEN 1 WHEN N'Adıyaman' THEN 2 WHEN N'Afyonkarahisar' THEN 3 WHEN N'Ağrı' THEN 4 WHEN N'Aksaray' THEN 5 WHEN N'Amasya' THEN 6 WHEN N'Ankara' THEN 7 WHEN N'Antalya' THEN 8 WHEN N'Ardahan' THEN 9 WHEN N'Artvin' THEN 10 WHEN N'Aydın' THEN 11 WHEN N'Balıkesir' THEN 12 WHEN N'Bartın' THEN 13 WHEN N'Batman' THEN 14 WHEN N'Bayburt' THEN 15 WHEN N'Bilecik' THEN 16 WHEN N'Bingöl' THEN 17 WHEN N'Bitlis' THEN 18 WHEN N'Bolu' THEN 19 WHEN N'Burdur' THEN 20 WHEN N'Bursa' THEN 21 WHEN N'Çanakkale' THEN 22 WHEN N'Çankırı' THEN 23 WHEN N'Çorum' THEN 24 WHEN N'Denizli' THEN 25 WHEN N'Diyarbakır' THEN 26 WHEN N'Düzce' THEN 27 WHEN N'Edirne' THEN 28 WHEN N'Elazığ' THEN 29 WHEN N'Erzincan' THEN 30 WHEN N'Erzurum' THEN 31 WHEN N'Eskişehir' THEN 32 WHEN N'Gaziantep' THEN 33 WHEN N'Giresun' THEN 34 WHEN N'Gümüşhane' THEN 35 WHEN N'Hakkari' THEN 36 WHEN N'Hatay' THEN 37 WHEN N'Iğdır' THEN 38 WHEN N'Isparta' THEN 39 WHEN N'İstanbul' THEN 40 WHEN N'İzmir' THEN 41 WHEN N'Kahramanmaraş' THEN 42 WHEN N'Karabük' THEN 43 WHEN N'Karaman' THEN 44 WHEN N'Kars' THEN 45 WHEN N'Kastamonu' THEN 46 WHEN N'Kayseri' THEN 47 WHEN N'Kırıkkale' THEN 48 WHEN N'Kırklareli' THEN 49 WHEN N'Kırşehir' THEN 50 WHEN N'Kilis' THEN 51 WHEN N'Kocaeli' THEN 52 WHEN N'Konya' THEN 53 WHEN N'Kütahya' THEN 54 WHEN N'Malatya' THEN 55 WHEN N'Manisa' THEN 56 WHEN N'Mardin' THEN 57 WHEN N'Mersin' THEN 58 WHEN N'Muğla' THEN 59 WHEN N'Muş' THEN 60 WHEN N'Nevşehir' THEN 61 WHEN N'Niğde' THEN 62 WHEN N'Ordu' THEN 63 WHEN N'Osmaniye' THEN 64 WHEN N'Rize' THEN 65 WHEN N'Sakarya' THEN 66 WHEN N'Samsun' THEN 67 WHEN N'Siirt' THEN 68 WHEN N'Sinop' THEN 69 WHEN N'Sivas' THEN 70 WHEN N'Şanlıurfa' THEN 71 WHEN N'Şırnak' THEN 72 WHEN N'Tekirdağ' THEN 73 WHEN N'Tokat' THEN 74 WHEN N'Trabzon' THEN 75 WHEN N'Tunceli' THEN 76 WHEN N'Uşak' THEN 77 WHEN N'Van' THEN 78 WHEN N'Yalova' THEN 79 WHEN N'Yozgat' THEN 80 WHEN N'Zonguldak' THEN 81 ELSE NULL END;
UPDATE RouteStops SET CityId = CASE City COLLATE Turkish_100_CI_AS WHEN N'Adana' THEN 1 WHEN N'Adıyaman' THEN 2 WHEN N'Afyonkarahisar' THEN 3 WHEN N'Ağrı' THEN 4 WHEN N'Aksaray' THEN 5 WHEN N'Amasya' THEN 6 WHEN N'Ankara' THEN 7 WHEN N'Antalya' THEN 8 WHEN N'Ardahan' THEN 9 WHEN N'Artvin' THEN 10 WHEN N'Aydın' THEN 11 WHEN N'Balıkesir' THEN 12 WHEN N'Bartın' THEN 13 WHEN N'Batman' THEN 14 WHEN N'Bayburt' THEN 15 WHEN N'Bilecik' THEN 16 WHEN N'Bingöl' THEN 17 WHEN N'Bitlis' THEN 18 WHEN N'Bolu' THEN 19 WHEN N'Burdur' THEN 20 WHEN N'Bursa' THEN 21 WHEN N'Çanakkale' THEN 22 WHEN N'Çankırı' THEN 23 WHEN N'Çorum' THEN 24 WHEN N'Denizli' THEN 25 WHEN N'Diyarbakır' THEN 26 WHEN N'Düzce' THEN 27 WHEN N'Edirne' THEN 28 WHEN N'Elazığ' THEN 29 WHEN N'Erzincan' THEN 30 WHEN N'Erzurum' THEN 31 WHEN N'Eskişehir' THEN 32 WHEN N'Gaziantep' THEN 33 WHEN N'Giresun' THEN 34 WHEN N'Gümüşhane' THEN 35 WHEN N'Hakkari' THEN 36 WHEN N'Hatay' THEN 37 WHEN N'Iğdır' THEN 38 WHEN N'Isparta' THEN 39 WHEN N'İstanbul' THEN 40 WHEN N'İzmir' THEN 41 WHEN N'Kahramanmaraş' THEN 42 WHEN N'Karabük' THEN 43 WHEN N'Karaman' THEN 44 WHEN N'Kars' THEN 45 WHEN N'Kastamonu' THEN 46 WHEN N'Kayseri' THEN 47 WHEN N'Kırıkkale' THEN 48 WHEN N'Kırklareli' THEN 49 WHEN N'Kırşehir' THEN 50 WHEN N'Kilis' THEN 51 WHEN N'Kocaeli' THEN 52 WHEN N'Konya' THEN 53 WHEN N'Kütahya' THEN 54 WHEN N'Malatya' THEN 55 WHEN N'Manisa' THEN 56 WHEN N'Mardin' THEN 57 WHEN N'Mersin' THEN 58 WHEN N'Muğla' THEN 59 WHEN N'Muş' THEN 60 WHEN N'Nevşehir' THEN 61 WHEN N'Niğde' THEN 62 WHEN N'Ordu' THEN 63 WHEN N'Osmaniye' THEN 64 WHEN N'Rize' THEN 65 WHEN N'Sakarya' THEN 66 WHEN N'Samsun' THEN 67 WHEN N'Siirt' THEN 68 WHEN N'Sinop' THEN 69 WHEN N'Sivas' THEN 70 WHEN N'Şanlıurfa' THEN 71 WHEN N'Şırnak' THEN 72 WHEN N'Tekirdağ' THEN 73 WHEN N'Tokat' THEN 74 WHEN N'Trabzon' THEN 75 WHEN N'Tunceli' THEN 76 WHEN N'Uşak' THEN 77 WHEN N'Van' THEN 78 WHEN N'Yalova' THEN 79 WHEN N'Yozgat' THEN 80 WHEN N'Zonguldak' THEN 81 ELSE NULL END;
UPDATE s SET CanBoard=CASE WHEN StopOrder=1 THEN 1 ELSE 0 END, CanAlight=CASE WHEN StopOrder=(SELECT MAX(x.StopOrder) FROM TripStops x WHERE x.TripId=s.TripId) THEN 1 ELSE 0 END FROM TripStops s;
UPDATE s SET CanBoard=CASE WHEN StopOrder=1 THEN 1 ELSE 0 END, CanAlight=CASE WHEN StopOrder=(SELECT MAX(x.StopOrder) FROM RouteStops x WHERE x.RouteId=s.RouteId) THEN 1 ELSE 0 END FROM RouteStops s;
""");
            migrationBuilder.Sql(IntermediateStopInventorySql.Stops.Replace("StopOrder,City,", "StopOrder,City,CityId,CanBoard,CanAlight,"));
            migrationBuilder.CreateIndex(
                name: "IX_TripStops_CityId_CanAlight_TripId_StopOrder",
                table: "TripStops",
                columns: new[] { "CityId", "CanAlight", "TripId", "StopOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_CityId_CanBoard_DepartureTime_TripId",
                table: "TripStops",
                columns: new[] { "CityId", "CanBoard", "DepartureTime", "TripId" });

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_TripId_CityId",
                table: "TripStops",
                columns: new[] { "TripId", "CityId" });

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_RouteId_CityId",
                table: "RouteStops",
                columns: new[] { "RouteId", "CityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM Reservations) THROW 51013, N'Rezervasyon geçmişi varken durak kimlikleri ve izinleri otomatik silinemez.', 1;");
            migrationBuilder.Sql(IntermediateStopInventorySql.Stops);
            migrationBuilder.DropIndex(
                name: "IX_TripStops_CityId_CanAlight_TripId_StopOrder",
                table: "TripStops");

            migrationBuilder.DropIndex(
                name: "IX_TripStops_CityId_CanBoard_DepartureTime_TripId",
                table: "TripStops");

            migrationBuilder.DropIndex(
                name: "IX_TripStops_TripId_CityId",
                table: "TripStops");

            migrationBuilder.DropIndex(
                name: "IX_RouteStops_RouteId_CityId",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "CanAlight",
                table: "TripStops");

            migrationBuilder.DropColumn(
                name: "CanBoard",
                table: "TripStops");

            migrationBuilder.DropColumn(
                name: "CityId",
                table: "TripStops");

            migrationBuilder.DropColumn(
                name: "CanAlight",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "CanBoard",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "CityId",
                table: "RouteStops");

            migrationBuilder.CreateIndex(
                name: "IX_TripStops_TripId_City",
                table: "TripStops",
                columns: new[] { "TripId", "City" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_RouteId_City",
                table: "RouteStops",
                columns: new[] { "RouteId", "City" },
                unique: true);
        }
    }
}
