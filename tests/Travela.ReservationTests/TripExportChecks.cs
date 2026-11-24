using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Travela.Api.Models;
using Travela.Api.Trips;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

internal static class TripExportChecks
{
    public static async Task Unit()
    {
        var checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        var now = DateTimeOffset.Parse("2026-11-01T00:30:00+03:00");
        foreach (var (period, start, end) in new[] {
            ("today", "2026-10-31T21:00:00Z", "2026-11-01T21:00:00Z"),
            ("yesterday", "2026-10-30T21:00:00Z", "2026-10-31T21:00:00Z"),
            ("last7days", "2026-10-25T21:00:00Z", "2026-11-01T21:00:00Z"),
            ("thismonth", "2026-10-31T21:00:00Z", "2026-11-30T21:00:00Z") }) {
            var f = new TripManagementFilters { CreatedPeriod = period }; var range = TripManagementQuery.CreationWindow(f, now);
            Check(range.StartUtc == DateTime.Parse(start).ToUniversalTime() && range.EndUtc == DateTime.Parse(end).ToUniversalTime(), period + " UTC window");
            var trips = new[] { new Trip { Id = 1, CreatedAt = range.StartUtc!.Value.AddTicks(-1) }, new Trip { Id = 2, CreatedAt = range.StartUtc }, new Trip { Id = 3, CreatedAt = range.EndUtc!.Value.AddTicks(-1) }, new Trip { Id = 4, CreatedAt = range.EndUtc }, new Trip { Id = 5, CreatedAt = null } }.AsQueryable();
            Check(TripManagementQuery.Filter(trips, f, range).Select(t => t.Id).SequenceEqual(new[] { 2, 3 }), period + " inclusive/exclusive");
        }
        var custom = new TripManagementFilters { CreatedPeriod = "custom", CreatedFrom = new(2026, 10, 31), CreatedTo = new(2026, 11, 1) };
        var window = TripManagementQuery.CreationWindow(custom, now);
        Check(window.StartUtc == new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc) && window.EndUtc == new DateTime(2026, 11, 1, 21, 0, 0, DateTimeKind.Utc), "custom inclusive days");
        var month = TripManagementQuery.CreationWindow(new() { CreatedPeriod = "thismonth" }, DateTimeOffset.Parse("2026-12-31T21:00:00Z"));
        Check(month.StartUtc == new DateTime(2026, 12, 31, 21, 0, 0, DateTimeKind.Utc) && month.EndUtc == new DateTime(2027, 1, 31, 21, 0, 0, DateTimeKind.Utc), "Turkey midnight / year transition");
        var beforeMidnight = TripManagementQuery.CreationWindow(new() { CreatedPeriod = "today" }, DateTimeOffset.Parse("2026-10-31T20:59:59Z"));
        Check(beforeMidnight.EndUtc == new DateTime(2026, 10, 31, 21, 0, 0, DateTimeKind.Utc), "before Turkey midnight");
        foreach (var invalid in new[] { new TripManagementFilters { CreatedPeriod = "invalid" }, new() { CreatedPeriod = "custom" }, new() { CreatedPeriod = "custom", CreatedFrom = new(2026, 11, 2), CreatedTo = new(2026, 11, 1) } }) {
            try { TripManagementQuery.CreationWindow(invalid, now); throw new Exception("Missing validation"); } catch (ArgumentException) { checks++; }
        }
        foreach (var direction in new[] { "asc", "desc" }) {
            var ordered = TripManagementQuery.Order(new[] { new Trip { Id = 1, CreatedAt = null }, new Trip { Id = 2, CreatedAt = now.UtcDateTime }, new Trip { Id = 3, CreatedAt = now.UtcDateTime.AddDays(-1) } }.AsQueryable(), new() { SortBy = "createdAt", SortDirection = direction }).Select(t => t.Id).ToArray();
            Check(ordered[^1] == 1 && ordered[0] == (direction == "asc" ? 3 : 2), "unknown dates last: " + direction);
        }
        using var output = new MemoryStream();
        await TripWorkbook.Write(output, Rows(), custom, now, default);
        Check(output.CanRead, "export stream remains readable"); output.Position = 0;
        using var book = SpreadsheetDocument.Open(output, false);
        var errors = new OpenXmlValidator().Validate(book).ToList();
        Check(errors.Count == 0, string.Join("; ", errors.Select(e => e.Description)));
        var row = SheetRows(book, "Seferler")[1]; var cells = row.Elements<Cell>().ToArray();
        Check(cells[1].DataType?.Value == CellValues.InlineString && cells[1].InlineString!.InnerText.StartsWith("=HYPERLINK"), "formula text safe");
        Check(!book.WorkbookPart!.WorksheetParts.SelectMany(p => p.Worksheet.Descendants<CellFormula>()).Any(), "no executable formulas");
        Check(cells[5].DataType?.Value == CellValues.Number && cells[5].StyleIndex?.Value == 2 && cells[8].DataType?.Value == CellValues.Number && cells[10].DataType?.Value == CellValues.Number, "typed dates/prices/counts");
        Check(cells[7].CellValue == null, "unknown date blank");
        Check(TripWorkbook.FileName(now) == "seferler_2026-11-01_00-30.xlsx", "Turkey filename");
        Console.WriteLine($"Trip export unit checks: {checks} passed.");
        static async IAsyncEnumerable<TripListDto> Rows() { await Task.CompletedTask; yield return new() { Id = 1, CompanyName = "=HYPERLINK(\"url\",\"Şöğüç\")", DepartureCity = "İstanbul", ArrivalCity = "Çeşme", DepartureTime = new(2026, 10, 31, 21, 30, 0, DateTimeKind.Utc), ArrivalTime = new(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc), Price = 123.45m, BusCapacity = 20, AvailableSeats = 17, CreatedAt = null }; }
    }
    public static async Task BeforeMigration(TravelaDbContext db)
    {
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().Single(m => m.EndsWith("_ExtendStaffProfiles")));
        var company = new Company { Name = "Tarih migration firması" }; var route = new Travela.Api.Models.Route { DepartureCity = "İstanbul", ArrivalCity = "Ankara" };
        var bus = new Bus { Company = company, PlateNumber = "34 MIG 99", NormalizedPlateNumber = "34MIG99", SeatCapacity = 10 };
        db.Trips.AddRange(new Trip { Company = company, Bus = bus, Route = route, DepartureTime = DateTime.UtcNow.AddDays(50), ArrivalTime = DateTime.UtcNow.AddDays(51), Price = 100, AvailableSeats = 10, CreatedAt = DateTime.MinValue }, new Trip { Company = company, Bus = bus, Route = route, DepartureTime = DateTime.UtcNow.AddDays(52), ArrivalTime = DateTime.UtcNow.AddDays(53), Price = 100, AvailableSeats = 10, CreatedAt = new DateTime(2025, 2, 3, 4, 5, 6) });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }
    public static async Task AfterMigration(TravelaDbContext db, Action<bool, string> check)
    {
        var dates = await db.Trips.Where(t => t.Company.Name == "Tarih migration firması").OrderBy(t => t.Id).Select(t => t.CreatedAt).ToListAsync();
        check(dates.Count == 2 && dates[0] == null && dates[1] == new DateTime(2025, 2, 3, 4, 5, 6), "Migration eski kayıtları korur; bilinmeyen tarihe tahmin atamaz");
    }
    private static List<Row> SheetRows(SpreadsheetDocument book, string name)
    {
        var sheet = book.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Single(s => s.Name == name);
        return ((WorksheetPart)book.WorkbookPart.GetPartById(sheet.Id!)).Worksheet.GetFirstChild<SheetData>()!.Elements<Row>().ToList();
    }
    public static async Task Run(TravelaDbContext db, HttpClient http, Action<bool, string> check, User admin, User companyUser, User passenger)
    {
        await Unit();
        var adminRole = await db.Roles.SingleAsync(r => r.Name == "Admin");
        var view = new Permission { Name = "trip.view" }; var create = new Permission { Name = "trip.create" };
        db.RolePermissions.AddRange(new RolePermission { Role = adminRole, Permission = view }, new RolePermission { Role = adminRole, Permission = create }, new RolePermission { Role = new Role { Name = "Company" }, Permission = view }); await db.SaveChangesAsync();
        await TripExportAuthorizationChecks.Run(db, check, admin, true);
        await TripExportAuthorizationChecks.Run(db, check, companyUser, true);
        async Task<JsonElement> Json(HttpResponseMessage response) { if (!response.IsSuccessStatusCode) throw new Exception(response.StatusCode + ": " + await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
        async Task<string> Login(User user) => (await Json(await http.PostAsJsonAsync("/api/auth/login", new { user.Email, password = "TestPass123!" }))).GetProperty("token").GetString()!;
        var token = await Login(admin); var companyToken = await Login(companyUser); var passengerToken = await Login(passenger);
        async Task<HttpResponseMessage> Send(string path, string? auth = null, string method = "GET", object? body = null) { using var request = new HttpRequestMessage(new HttpMethod(method), "/api/trips" + path); if (auth != null) request.Headers.Authorization = new("Bearer", auth); if (body != null) request.Content = JsonContent.Create(body); return await http.SendAsync(request); }
        var f = new TripManagementFilters { CreatedPeriod = "today" }; var now = DateTimeOffset.UtcNow; var range = TripManagementQuery.CreationWindow(f, now);
        var company = new Company { Name = "=HYPERLINK(\"x\",\"Şoför Çığ\")" }; var route = new Travela.Api.Models.Route { DepartureCity = "İstanbul", ArrivalCity = "Çeşme" };
        var buses = Enumerable.Range(1, 18).Select(i => new Bus { Company = company, PlateNumber = "34 EXPORT " + i, NormalizedPlateNumber = "34EXPORT" + i, SeatCapacity = 20, SeatLayout = "2+2", SeatPlanJson = SeatPlans.Write(SeatPlans.Create("2+2", 20)) }).ToArray();
        var dates = Enumerable.Range(0, 13).Select(i => (DateTime?)range.StartUtc!.Value.AddMinutes(i)).Concat([range.StartUtc!.Value.AddTicks(-1), range.EndUtc!.Value.AddTicks(-1), range.EndUtc, null, now.AddDays(-10).UtcDateTime]).ToArray();
        var trips = dates.Select((date, i) => new Trip { Company = company, Bus = buses[i], Route = route, DepartureTime = now.UtcDateTime.AddDays(100 + i), ArrivalTime = now.UtcDateTime.AddDays(100 + i).AddHours(5), Price = 125.50m, AvailableSeats = 17, SeatCapacitySnapshot = 20, SellableSeatCount = 19, CreatedAt = date }).ToArray();
        db.Trips.AddRange(trips); await db.SaveChangesAsync();
        var query = "companyId=" + company.Id;
        var first = await Json(await Send("?" + query + "&pageSize=10&createdPeriod=today&sortBy=createdAt&sortDirection=asc", token));
        check(first.GetProperty("items").GetArrayLength() == 10 && first.GetProperty("totalCount").GetInt32() == 14, "Bugün filtresi Türkiye günü sınırlarıyla çalışır; sayfalama toplamı korur");
        foreach (var period in new[] { "yesterday", "last7days", "thismonth" }) {
            var window = TripManagementQuery.CreationWindow(new() { CreatedPeriod = period }, now);
            var expected = trips.Where(t => t.CreatedAt >= window.StartUtc && t.CreatedAt < window.EndUtc).Select(t => t.Id).Order().ToArray();
            var list = await Json(await Send("?" + query + "&createdPeriod=" + period + "&pageSize=100", token));
            check(list.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetInt32()).Order().SequenceEqual(expected), "SQL oluşturma sınırları: " + period);
        }
        foreach (var direction in new[] { "asc", "desc" }) {
            var list = await Json(await Send("?" + query + "&pageSize=100&sortBy=createdAt&sortDirection=" + direction, token)); var rows = list.GetProperty("items").EnumerateArray().ToArray();
            check(rows[^1].GetProperty("createdAt").ValueKind == JsonValueKind.Null, "Bilinmeyen tarih sonda: " + direction);
            var excel = await Send("/export?" + query + "&pageNumber=2&pageSize=1&sortBy=createdAt&sortDirection=" + direction, token);
            check(excel.IsSuccessStatusCode && excel.Content.Headers.ContentType?.MediaType == TripWorkbook.ContentType && excel.Headers.CacheControl?.NoStore == true, "Yetkili admin gerçek XLSX indirir");
            using var memory = new MemoryStream(await excel.Content.ReadAsByteArrayAsync()); using var book = SpreadsheetDocument.Open(memory, false);
            var exported = SheetRows(book, "Seferler").Skip(1).ToArray();
            check(exported.Length == trips.Length && exported.Select(r => int.Parse(r.Elements<Cell>().First().CellValue!.Text)).SequenceEqual(rows.Select(t => t.GetProperty("id").GetInt32())), "Excel tüm eşleşmeleri listeyle aynı sıralamada içerir; sayfalama yok sayılır");
            var cells = exported.First(r => r.Elements<Cell>().First().CellValue!.Text == trips[0].Id.ToString()).Elements<Cell>().ToArray();
            check(cells.Length == 14 && cells[1].InlineString!.InnerText == company.Name && cells[8].DataType?.Value == CellValues.Number && decimal.Parse(cells[8].CellValue!.Text, CultureInfo.InvariantCulture) == 125.50m, "Türkçe ve formül benzeri metin korunur; fiyat sayısaldır");
            check(cells[10].CellValue!.Text == "20" && cells[11].CellValue!.Text == "17" && cells[12].CellValue!.Text == "3", "Koltuklar liste envanteriyle aynı; bloke ve kullanım doluya dahildir");
            var date = DateTime.FromOADate(double.Parse(cells[7].CellValue!.Text, CultureInfo.InvariantCulture));
            check(Math.Abs((date - TripManagementQuery.LocalTime(trips[0].CreatedAt!.Value)).TotalMilliseconds) < 2, "Excel tarihi Türkiye yerel saatini temsil eder");
            check(exported[^1].Elements<Cell>().ElementAt(7).CellValue == null, "Bilinmeyen oluşturulma hücresi boş kalır");
            var sheet = book.WorkbookPart!.WorksheetParts.First(p => p.Worksheet.GetFirstChild<AutoFilter>() != null).Worksheet;
            check(sheet.Descendants<Pane>().Single().State?.Value == PaneStateValues.Frozen && sheet.GetFirstChild<AutoFilter>()!.Reference == "A1:N19", "Excel başlığı sabit ve otomatik filtre açık");
            var info = SheetRows(book, "Rapor Bilgisi");
            check(info.Any(r => r.InnerText.Contains("Europe/Istanbul")) && info.Any(r => r.InnerText.Contains("Toplam kayıt sayısı18")) && info.Any(r => r.InnerText.Contains(direction == "asc" ? "Artan" : "Azalan")), "Rapor bilgisi saat dilimini, filtreleri, sıralamayı ve sayıyı içerir");
            if (direction == "asc") { Directory.CreateDirectory("tmp/trip-export"); await File.WriteAllBytesAsync("tmp/trip-export/verified-seferler.xlsx", memory.ToArray()); }
        }
        var day = DateOnly.FromDateTime(TripManagementQuery.LocalTime(now.UtcDateTime));
        var combined = query + $"&createdPeriod=custom&createdFrom={day:yyyy-MM-dd}&createdTo={day:yyyy-MM-dd}&departureCity=İstanbul&arrivalCity=Çeşme&busPlate=34%20EXPORT&minPrice=125&maxPrice=126&status=Scheduled&search=EXPORT&departureFrom={trips[0].DepartureTime:yyyy-MM-dd}&departureTo={trips[12].DepartureTime:yyyy-MM-dd}&sortBy=price&sortDirection=desc";
        var combinedList = await Json(await Send("?" + combined + "&pageSize=100", token));
        var combinedExcel = await Send("/export?" + combined, token); using var combinedMemory = new MemoryStream(await combinedExcel.Content.ReadAsByteArrayAsync()); using var combinedBook = SpreadsheetDocument.Open(combinedMemory, false);
        check(combinedList.GetProperty("totalCount").GetInt32() == 13 && SheetRows(combinedBook, "Seferler").Skip(1).Select(r => int.Parse(r.Elements<Cell>().First().CellValue!.Text)).SequenceEqual(combinedList.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetInt32())), "Tüm mevcut filtreler özel tarihle birleşir; liste ve Excel sonuçları eşittir");
        foreach (var bad in new[] { "createdPeriod=custom", "createdPeriod=custom&createdFrom=2026-11-02&createdTo=2026-11-01", "createdPeriod=bad" }) check((await Send("?" + bad, token)).StatusCode == HttpStatusCode.BadRequest && (await Send("/export?" + bad, token)).StatusCode == HttpStatusCode.BadRequest, "Liste ve Excel aynı tarih doğrulamasını uygular");
        foreach (var (auth, status) in new[] { ((string?)null, HttpStatusCode.Unauthorized), (companyToken, HttpStatusCode.Forbidden), (passengerToken, HttpStatusCode.Forbidden) }) check((await Send("/export?" + query, auth)).StatusCode == status, "Anonim/yolcu ve başka firmanın Excel indirmesi engellenir");
        check((await Send("?" + query, companyToken)).StatusCode == HttpStatusCode.Forbidden, "Firma başka firmanın sefer listesini okuyamaz");
        var companyExporter = new User { FullName = "Firma Excel Yetkilisi", Email = "company-export@travela.test", Role = "Company", CompanyId = company.Id, PasswordHash = companyUser.PasswordHash };
        db.Users.Add(companyExporter); await db.SaveChangesAsync(); var ownCompanyToken = await Login(companyExporter);
        foreach (var ownQuery in new[] { "sortBy=createdAt&sortDirection=desc", query + "&sortBy=createdAt&sortDirection=desc", combined[(query.Length + 1)..] }) {
            var list = await Json(await Send("?" + ownQuery + "&pageSize=100", ownCompanyToken));
            var export = await Send("/export?" + ownQuery + "&pageSize=1&pageNumber=2", ownCompanyToken);
            check(export.IsSuccessStatusCode && export.Content.Headers.ContentType?.MediaType == TripWorkbook.ContentType, "Yetkili firma gerçek XLSX indirebilir");
            using var ownMemory = new MemoryStream(await export.Content.ReadAsByteArrayAsync()); using var ownBook = SpreadsheetDocument.Open(ownMemory, false);
            var ownRows = SheetRows(ownBook, "Seferler").Skip(1).ToArray();
            check(ownRows.Select(r => int.Parse(r.Elements<Cell>().First().CellValue!.Text)).SequenceEqual(list.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetInt32())) &&
                ownRows.Length > 10 && ownRows.All(r => r.Elements<Cell>().ElementAt(1).InnerText == company.Name), "Firma Excel'i yalnız kendi tüm eşleşmelerini, listeyle aynı filtre ve sıralamayla içerir");
            var scopeRow = SheetRows(ownBook, "Rapor Bilgisi").Single(r => r.Elements<Cell>().First().InnerText == "Firma ID");
            check(scopeRow.Elements<Cell>().ElementAt(1).InnerText == company.Id.ToString(), "Firma rapor bilgisi etkin firma kapsamını gösterir");
        }
        check((await Send("/export?companyId=" + companyUser.CompanyId, ownCompanyToken)).StatusCode == HttpStatusCode.Forbidden, "Firma URL'de companyId değiştirerek başka firmanın Excel'ini indiremez");
        db.RolePermissions.Add(new RolePermission { Role = new Role { Name = "Passenger" }, Permission = view }); await db.SaveChangesAsync();
        check((await Send("/export?" + query, passengerToken)).StatusCode == HttpStatusCode.Forbidden &&
            (await Send("?" + query, passengerToken)).StatusCode == HttpStatusCode.Forbidden,
            "trip.view verilmiş olsa da yolcu liste ve Excel'e erişemez; veritabanındaki rol denetimi korunur");
        // Copy uses the existing create contract and deliberately does not accept an original CreatedAt.
        var body = new CreateTripDto { CompanyId = company.Id, BusId = buses[0].Id, RouteId = route.Id, DepartureTime = now.UtcDateTime.AddDays(150), ArrivalTime = now.UtcDateTime.AddDays(150).AddHours(5), Price = 125.50m };
        var created = await Json(await Send("", token, "POST", body)); var copiedId = created.GetProperty("tripId").GetInt32();
        var copied = await db.Trips.AsNoTracking().SingleAsync(t => t.Id == copiedId);
        check(copied.CreatedAt.HasValue && copied.CreatedAt >= now.UtcDateTime && copied.CreatedAt != trips[0].CreatedAt, "Kopyalanan sefer kendi sunucu oluşturulma tarihini alır");
        body.Price = 130;
        await Json(await Send("/" + copiedId, token, "PUT", body));
        check((await db.Trips.AsNoTracking().SingleAsync(t => t.Id == copiedId)).CreatedAt == copied.CreatedAt, "Düzenleme oluşturulma tarihini değiştirmez");
        var permission = await db.RolePermissions.SingleAsync(r => r.RoleId == adminRole.Id && r.PermissionId == view.Id); db.RolePermissions.Remove(permission); await db.SaveChangesAsync();
        check((await Send("/export?" + query, token)).StatusCode == HttpStatusCode.Forbidden, "trip.view izni olmayan admin de Excel indiremez");
        await TripExportAuthorizationChecks.Run(db, check, admin, false);
        var companyPermission = await db.RolePermissions.SingleAsync(r => r.Role.Name == "Company" && r.PermissionId == view.Id); db.RolePermissions.Remove(companyPermission); await db.SaveChangesAsync();
        check((await Send("/export", ownCompanyToken)).StatusCode == HttpStatusCode.Forbidden, "trip.view izni olmayan firma da Excel indiremez");
        await TripExportAuthorizationChecks.Run(db, check, companyExporter, false);
    }
}
