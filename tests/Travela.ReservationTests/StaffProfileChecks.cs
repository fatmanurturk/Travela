using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Travela.Api.Staff;
using Travela.Api.Models;
using Travela.Data;
using Travela.Models;

internal static class StaffProfileChecks
{
    static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/1cAAAAASUVORK5CYII=");
    public static void Unit()
    {
        var today = new DateOnly(2026, 11, 24);
        void Check(bool ok) { if (!ok) throw new Exception("Document boundary failed"); }
        foreach (var (offset, status, days) in new[] { (31, "Valid", 31), (30, "Expiring", 30), (1, "Expiring", 1), (0, "Expiring", 0), (-1, "Expired", -1) }) {
            var value = StaffDates.Validity(today.AddDays(offset), false, today); Check(value.Status == status && value.Days == days);
        }
        Check(StaffDates.Validity(today, false, today).Message == "Bugün sona eriyor");
        Check(StaffDates.Validity(null, true, today).Status == "Indefinite");
        Check(StaffDates.Today(new FixedClock(new DateTimeOffset(2026, 11, 23, 21, 0, 0, TimeSpan.Zero))) == today);
        Check(StaffDates.Validity(today, false, today.AddDays(1)).Status == "Expired");
        Console.WriteLine("8 document date/timezone checks passed.");
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    public static async Task BeforeMigration(TravelaDbContext db)
    {
        await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().Single(m => m.EndsWith("_AddCompanyStaffAndTripCrew")));
        // Simulate a real staff row in the old schema before the additive upgrade.
        var company = new Company { Name = "Eski personel firması" }; db.Companies.Add(company); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO CompanyStaff (CompanyId, FullName, Duty, IsActive) VALUES ({company.Id}, N'Eski Personel', 'Driver', 1)");
    }
    public static async Task AfterMigration(TravelaDbContext db, Action<bool, string> check)
    {
        var legacy = await db.CompanyStaff.SingleAsync(s => s.FullName == "Eski Personel");
        check(legacy.CareerStartYear == null && legacy.EmploymentStartDate == null && legacy.Languages.Length == 0 && legacy.IsActive, "Migration eski personeli ve isteğe bağlı alanları korur");
    }
    public static async Task Run(TravelaDbContext db, HttpClient http, Action<bool, string> check, User owner, User other, User passenger)
    {
        Unit();
        async Task<JsonElement> Json(HttpResponseMessage response) { if (!response.IsSuccessStatusCode) throw new Exception(response.StatusCode + ": " + await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
        async Task<string> Login(User user) => (await Json(await http.PostAsJsonAsync("/api/auth/login", new { user.Email, password = "TestPass123!" }))).GetProperty("token").GetString()!;
        var token = await Login(owner); var foreign = await Login(other); var passengerToken = await Login(passenger);
        async Task<HttpResponseMessage> Send(string method, string path, string? auth = null, HttpContent? content = null) {
            using var request = new HttpRequestMessage(new HttpMethod(method), "/api/company/staff" + path) { Content = content };
            if (auth != null) request.Headers.Authorization = new("Bearer", auth);
            return await http.SendAsync(request);
        }
        MultipartFormDataContent Profile(object value, byte[]? bytes = null, string mime = "image/png", string name = "photo.png", bool remove = false) {
            var form = new MultipartFormDataContent(); form.Add(new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))), "profile"); form.Add(new StringContent(remove.ToString()), "removePhoto");
            if (bytes != null) { var part = new ByteArrayContent(bytes); part.Headers.ContentType = new(mime); form.Add(part, "photo", name); } return form;
        }
        var today = StaffDates.Today(TimeProvider.System);
        object Body(string duty = "Driver") => new { fullName = "Profil Personeli", duty, isActive = true, careerStartYear = today.Year - 8, employmentStartDate = today.AddYears(-1), emergencyContactName = "Acil Kişi", emergencyContactRelationship = "Kardeşi", emergencyContactPhone = "0532 123 45 67", languages = new[] { "Türkçe", "İngilizce" }, licenseClasses = new[] { "B", "D" }, vehicleTypes = new[] { "Otobüs" }, serviceExperience = true, serviceExperienceNotes = "Servis eğitimi", baggageExperience = false, passengerCommunicationExperience = true, passengerCommunicationExperienceNotes = "İletişim eğitimi" };
        var created = await Json(await Send("POST", "/profile", token, Profile(Body(), Png))); var id = created.GetProperty("id").GetInt32();
        var detail = await Json(await Send("GET", $"/{id}", token));
        check(detail.GetProperty("experienceYears").GetInt32() == 8 && detail.GetProperty("languages").GetArrayLength() == 2 && detail.GetProperty("hasPhoto").GetBoolean(), "Şoför profili ve fotoğraf atomik kaydedilir; deneyim hesaplanır");
        var photo = await Send("GET", $"/{id}/photo", token);
        check((await photo.Content.ReadAsByteArrayAsync()).SequenceEqual(Png) && photo.Headers.CacheControl?.NoStore == true, "Fotoğraf yetkili oturumla ve önbelleksiz alınır");
        await Json(await Send("PUT", $"/{id}/profile", token, Profile(Body("Attendant"))));
        detail = await Json(await Send("GET", $"/{id}", token));
        check(detail.GetProperty("serviceExperience").GetBoolean() && !detail.GetProperty("baggageExperience").GetBoolean() && detail.GetProperty("passengerCommunicationExperienceNotes").GetString() == "İletişim eğitimi", "Muavin deneyimleri ayrı ayrı ve açıklamalarıyla güncellenir");
        var attendant = await Json(await Send("POST", "/profile", token, Profile(Body("Attendant"))));
        check(attendant.GetProperty("duty").GetString() == "Attendant", "Muavin yeni profil alanlarıyla oluşturulur");
        await Json(await Send("PUT", $"/{id}", token, JsonContent.Create(new { fullName = "Eski istemci", duty = "Attendant", isActive = false })));
        detail = await Json(await Send("GET", $"/{id}", token));
        check(detail.GetProperty("careerStartYear").GetInt32() == today.Year - 8 && detail.GetProperty("languages").GetArrayLength() == 2, "Eski JSON istemcisi ve pasife alma yeni profil alanlarını silmez");
        foreach (var value in new object[] { new { fullName = "Gelecek yıl", duty = "Driver", careerStartYear = today.Year + 1 }, new { fullName = "Gelecek tarih", duty = "Driver", employmentStartDate = today.AddDays(1) } })
            check((await Send("POST", "/profile", token, Profile(value))).StatusCode == HttpStatusCode.BadRequest, "Gelecek meslek yılı / işe giriş tarihi reddedilir");
        var before = await db.CompanyStaff.CountAsync();
        foreach (var (bytes, mime, name) in new[] { (Encoding.UTF8.GetBytes("fake png"), "image/png", "fake.png"), (Png, "image/jpeg", "wrong.jpg"), (new byte[StaffFiles.PhotoLimit + 1], "image/png", "large.png"), (Encoding.UTF8.GetBytes("%PDF-1.7 test"), "application/pdf", "photo.pdf") })
            check((await Send("POST", "/profile", token, Profile(Body(), bytes, mime, name))).StatusCode == HttpStatusCode.BadRequest, "Sahte tür / yanlış içerik / büyük fotoğraf reddedilir");
        check(await db.CompanyStaff.CountAsync() == before && await db.StaffPhotos.CountAsync() == 1, "Başarısız profil yüklemeleri sahipsiz kayıt veya dosya bırakmaz");
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER StaffPhotoFailureTest ON StaffPhotos AFTER INSERT, UPDATE AS THROW 51000, 'Simulated storage failure', 1;");
        try {
            check((await Send("POST", "/profile", token, Profile(Body(), Png))).StatusCode == HttpStatusCode.InternalServerError &&
                await db.CompanyStaff.CountAsync() == before && await db.StaffPhotos.CountAsync() == 1, "Dosya veritabanı kaydı başarısız olunca yeni personel de geri alınır");
            check((await Send("PUT", $"/{id}/profile", token, Profile(Body(), Png.Concat(new byte[] { 0 }).ToArray()))).StatusCode == HttpStatusCode.InternalServerError &&
                (await db.CompanyStaff.AsNoTracking().SingleAsync(s => s.Id == id)).FullName == "Eski istemci" &&
                (await (await Send("GET", $"/{id}/photo", token)).Content.ReadAsByteArrayAsync()).SequenceEqual(Png), "Dosya saklama hatasında mevcut profil ve fotoğraf korunur");
        } finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER StaffPhotoFailureTest"); }
        var jpeg = new byte[] { 255, 216, 255, 224, 1, 2, 255, 217 };
        await Json(await Send("PUT", $"/{id}/profile", token, Profile(Body(), jpeg, "image/jpeg", "new.jpg")));
        check((await (await Send("GET", $"/{id}/photo", token)).Content.ReadAsByteArrayAsync()).SequenceEqual(jpeg), "Fotoğraf değiştirilince eski dosya yerini yeni dosyaya bırakır");
        await Json(await Send("PUT", $"/{id}/profile", token, Profile(Body(), remove: true)));
        check((await Send("GET", $"/{id}/photo", token)).StatusCode == HttpStatusCode.NotFound && !await db.StaffPhotos.AnyAsync(p => p.StaffId == id), "Fotoğraf kaldırılır ve dosyası kalmaz");
        await Json(await Send("PUT", $"/{id}/profile", token, Profile(Body(), Png)));
        var webp = Convert.FromBase64String("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA");
        await Json(await Send("PUT", $"/{id}/profile", token, Profile(Body(), webp, "image/webp", "photo.webp")));
        check((await (await Send("GET", $"/{id}/photo", token)).Content.ReadAsByteArrayAsync()).SequenceEqual(webp), "WebP profil fotoğrafı desteklenir");
        check((await Send("PUT", $"/{id}/profile", foreign, Profile(Body(), Png))).StatusCode == HttpStatusCode.NotFound, "Başka firma profil veya fotoğrafı değiştiremez");
        MultipartFormDataContent Document(string name, DateOnly? expires, bool indefinite = false, byte[]? data = null, string mime = "application/pdf", string fileName = "certificate.pdf", bool withFile = true) {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(name), "name"); form.Add(new StringContent("Professional"), "documentType"); form.Add(new StringContent(today.AddYears(-1).ToString("yyyy-MM-dd")), "issuedOn"); form.Add(new StringContent(indefinite.ToString()), "isIndefinite");
            if (expires.HasValue) form.Add(new StringContent(expires.Value.ToString("yyyy-MM-dd")), "expiresOn");
            if (withFile) { var part = new ByteArrayContent(data ?? Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF")); part.Headers.ContentType = new(mime); form.Add(part, "file", fileName); } return form;
        }
        var documents = new List<int>();
        foreach (var (name, expiry, indefinite, status) in new[] { ("31 gün", today.AddDays(31), false, "Valid"), ("30 gün", today.AddDays(30), false, "Expiring"), ("Bugün", today, false, "Expiring"), ("Dün", today.AddDays(-1), false, "Expired"), ("Süresiz", today, true, "Indefinite") }) {
            var doc = await Json(await Send("POST", $"/{id}/documents", token, Document(name, expiry, indefinite))); documents.Add(doc.GetProperty("id").GetInt32());
            detail = await Json(await Send("GET", $"/{id}", token)); var stored = detail.GetProperty("documents").EnumerateArray().Single(d => d.GetProperty("id").GetInt32() == documents[^1]);
            check(stored.GetProperty("validity").GetProperty("status").GetString() == status && (name != "Bugün" || stored.GetProperty("validity").GetProperty("message").GetString() == "Bugün sona eriyor"), $"Belge sınırı: {name} → {status}");
        }
        var docPath = $"/{id}/documents/{documents[0]}";
        var download = await Send("GET", docPath + "/file?download=true", token);
        check(download.IsSuccessStatusCode && download.Content.Headers.ContentDisposition?.DispositionType == "attachment" && download.Headers.CacheControl?.NoStore == true, "Belge görüntüleme ve indirme güvenli API üzerinden çalışır");
        foreach (var (auth, expected) in new[] { ((string?)null, HttpStatusCode.Unauthorized), (passengerToken, HttpStatusCode.Forbidden), (foreign, HttpStatusCode.NotFound) }) {
            check((await Send("GET", docPath + "/file", auth)).StatusCode == expected && (await Send("GET", $"/{id}/photo", auth)).StatusCode == expected, "Anonim, yolcu ve yabancı firma dosyalara erişemez");
            check((await Send("PUT", docPath, auth, Document("Yetkisiz", today))).StatusCode == expected && (await Send("DELETE", docPath + "?confirm=true", auth)).StatusCode == expected, "Yetkisiz belge değiştirme ve silme engellenir");
        }
        foreach (var (data, mime, name) in new[] { (new byte[StaffFiles.DocumentLimit + 1], "application/pdf", "big.pdf"), (Encoding.UTF8.GetBytes("fake"), "application/pdf", "fake.pdf"), (Png, "image/png", "image.exe") })
            check((await Send("PUT", docPath, token, Document("Hatalı değişiklik", today, data: data, mime: mime, fileName: name))).StatusCode == HttpStatusCode.BadRequest, "Geçersiz belge dosyası reddedilir");
        check(await db.StaffDocuments.CountAsync(d => d.StaffId == id) == 5 && (await db.StaffDocuments.AsNoTracking().SingleAsync(d => d.Id == documents[0])).Name == "31 gün", "Başarısız belge güncellemesi metadata ve eski dosyayı korur");
        check((await Send("POST", $"/{id}/documents", token, Document("Eksik tarih", null))).StatusCode == HttpStatusCode.BadRequest, "Süreli belgede bitiş tarihi zorunludur");
        await Json(await Send("PUT", docPath, token, Document("Güncellenmiş sertifika", null, true, withFile: false)));
        check((await db.StaffDocuments.AsNoTracking().SingleAsync(d => d.Id == documents[0])).IsIndefinite, "Belge dosyayı koruyarak süresize güncellenebilir");
        await Json(await Send("PUT", docPath, token, Document("PNG sertifika", today.AddDays(31), data: Png, mime: "image/png", fileName: "new.png")));
        check((await (await Send("GET", docPath + "/file", token)).Content.ReadAsByteArrayAsync()).SequenceEqual(Png), "Belge dosyası değiştirilir; yeni dosya görüntülenir");
        foreach (var filter in new[] { "Expiring", "Expired" }) {
            var list = await Json(await Send("GET", "?documentStatus=" + filter, token));
            check(list.EnumerateArray().Any(s => s.GetProperty("id").GetInt32() == id), "Personel belge durumuna göre filtrelenir: " + filter);
        }
        using var attentionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/company/attention"); attentionRequest.Headers.Authorization = new("Bearer", token);
        var attention = await Json(await http.SendAsync(attentionRequest));
        var warning = attention.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("id").GetString() == "staff-documents");
        check(warning.GetProperty("count").GetInt32() == 3 && warning.GetProperty("items").EnumerateArray().All(i => i.GetProperty("href").GetString() == $"/company/staff?detail={id}" && i.GetProperty("description").GetString()!.Contains("Son geçerlilik")), "Yönetim paneli güncel belge uyarılarını ve profil bağlantılarını gösterir");
        check((await Send("DELETE", docPath, token)).StatusCode == HttpStatusCode.BadRequest, "Belge silme onay gerektirir");
        await Json(await Send("DELETE", docPath + "?confirm=true", token));
        check(!await db.StaffDocuments.AnyAsync(d => d.Id == documents[0]) && (await Send("GET", docPath + "/file", token)).StatusCode == HttpStatusCode.NotFound, "Onaylı silme belgeyi ve dosyasını birlikte siler");
        var permission = await db.RolePermissions.Include(p => p.Role).Include(p => p.Permission).SingleAsync(p => p.Role.Name == "Company" && p.Permission.Name == "trip.view");
        db.RolePermissions.Remove(permission); await db.SaveChangesAsync();
        check((await Send("GET", $"/{id}/photo", token)).StatusCode == HttpStatusCode.Forbidden && (await Send("GET", $"/{id}/documents/{documents[1]}/file", token)).StatusCode == HttpStatusCode.Forbidden, "trip.view izni kaldırılan firma dosyaları da okuyamaz");
    }
}
