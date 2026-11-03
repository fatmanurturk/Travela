using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
Directory.SetCurrentDirectory(root);
using var source = new TravelaDbContextFactory().CreateDbContext([]);
if (args.Contains("--verify-existing"))
{
    if ((await source.Database.GetPendingMigrationsAsync()).Any()) throw new Exception("Bekleyen migration var.");
    using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5173") };
    foreach (var t in await source.Trips.AsNoTracking().ToListAsync())
    {
        (await http.GetAsync($"/api/trips/public/{t.Id}")).EnsureSuccessStatusCode();
        Console.WriteLine($"Mevcut sefer #{t.Id}: proxy HTTP 200");
    }
    Console.WriteLine($"Eski ücretsiz kayıt: {await source.Reservations.CountAsync(r => r.Kind == "Legacy")}; bilet: {await source.Reservations.CountAsync(r => r.Kind == "Ticket")}. Kayıt değiştirilmedi.");
    return;
}
var database = "TravelaPaymentTests_" + Guid.NewGuid().ToString("N");
var cs = new SqlConnectionStringBuilder(source.Database.GetConnectionString()) { InitialCatalog = database };
var options = new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer(cs.ConnectionString).Options;
await using var db = new TravelaDbContext(options);
Process? server = null;
var log = new System.Text.StringBuilder();
var passed = 0;
try
{
    await db.Database.MigrateAsync();
    var company = new Company { Name = "Test Firma" };
    var otherCompany = new Company { Name = "Diğer Firma" };
    var bus = new Bus { Company = company, SeatCapacity = 20, PlateNumber = "34 TEST 001", NormalizedPlateNumber = "34TEST001" };
    var route = new Travela.Api.Models.Route { DepartureCity = "İstanbul", ArrivalCity = "Ankara", EstimatedDurationMinutes = 360 };
    var trip = new Trip { Company = company, Bus = bus, Route = route, DepartureTime = DateTime.UtcNow.AddDays(3), ArrivalTime = DateTime.UtcNow.AddDays(3).AddHours(6), Price = 500, AvailableSeats = 19 };
    var u1 = User("one"); var u2 = User("two"); var admin = User("admin", "Admin");
    u1.FullName = "Çağrı Şükrü Işık Öztürk " + new string('Ğ', 44) + " Çalışkan Uzunsoyad";
    var c1 = User("company", "Company"); var c2 = User("other-company", "Company");
    db.AddRange(company, otherCompany, trip, u1, u2, admin, c1, c2);
    var role = new Role { Name = "Admin" };
    db.RolePermissions.Add(new RolePermission { Role = role, Permission = new Permission { Name = "trip.update" } });
    await db.SaveChangesAsync(); c1.CompanyId = company.Id; c2.CompanyId = otherCompany.Id; await db.SaveChangesAsync();
    var legacyNumber = Guid.NewGuid().ToString("N"); var created = DateTime.UtcNow;
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Reservations (ReservationNumber, TripId, PassengerId, SeatNumber, Price, CreatedAt, Status) VALUES ({legacyNumber}, {trip.Id}, {u1.Id}, 20, 500, {created}, N'Active')");
    await db.GetService<IMigrator>().MigrateAsync("20261102101401_AddReservationCancellation");
    await db.Database.MigrateAsync();
    Check(await db.Reservations.AnyAsync(r => r.ReservationNumber == legacyNumber && r.Kind == "Legacy" && r.Pnr == null) && !await db.PaymentOrders.AnyAsync(), "Migration eski ücretsiz kaydı korudu; ödenmiş bilete çevirmedi");
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var config = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(root, "backend"), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(Path.Combine(root, "backend", "bin", config, "net10.0", "Travela.API.dll"));
    start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
    start.Environment["ConnectionStrings__DefaultConnection"] = cs.ConnectionString;
    start.Environment["Jwt__Key"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    start.Environment["Jwt__Issuer"] = "PaymentTests"; start.Environment["Jwt__Audience"] = "PaymentTests";
    start.Environment["Payments__RecoveryEnabled"] = "false";
    start.Environment["Logging__LogLevel__Default"] = "Warning";
    server = Process.Start(start)!;
    server.OutputDataReceived += (_, e) => { lock (log) log.AppendLine(e.Data); };
    server.ErrorDataReceived += (_, e) => { lock (log) log.AppendLine(e.Data); };
    server.BeginOutputReadLine(); server.BeginErrorReadLine();
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(30) };
    var ready = false;
    for (var i = 0; i < 60; i++) { try { ready = (await http.GetAsync("/api/test")).IsSuccessStatusCode; if (ready) break; } catch (HttpRequestException) { } if (server.HasExited) throw new Exception(log.ToString()); await Task.Delay(200); }
    Check(ready, "Test API çalışıyor");
    var token1 = await Login(u1); var token2 = await Login(u2); var adminToken = await Login(admin); var companyToken = await Login(c1); var otherToken = await Login(c2);
    Check((await Send("POST", "/api/reservations", token1)).StatusCode == HttpStatusCode.Gone, "Bağımsız ücretsiz rezervasyon endpoint'i kapalı (410)");
    Check((await Send("POST", "/api/tickets/checkout", null, new { tripId = trip.Id, seatNumber = 1, requestKey = Guid.NewGuid() })).StatusCode == HttpStatusCode.Unauthorized, "Kimliksiz satın alma engellendi");
    var key = Guid.NewGuid();
    var order = await Start(1, key);
    Check(order.Amount == 500 && order.Pnr == null && order.IsTest && order.PaymentStatus == "Pending", "Tutar backend'den, ödeme öncesi PNR yok, test etiketi mevcut");
    Check((await Send("GET", $"/api/tickets/{order.Id}/pdf", token1)).StatusCode == HttpStatusCode.NotFound &&
        (await Send("GET", $"/api/tickets/{order.Id}", token1)).StatusCode == HttpStatusCode.NotFound, "Ödemesi tamamlanmamış sipariş için bilet detayı ve PDF üretilmedi");
    Check((await Send("GET", $"/api/tickets/{legacyNumber}/pdf", token1)).StatusCode == HttpStatusCode.NotFound, "Eski ücretsiz kayda bilet PDF'si üretilmedi");
    Check((await Start(1, key)).Id == order.Id && await db.PaymentOrders.CountAsync() == 1 && await db.LocalPaymentRecords.CountAsync() == 1, "Tekrar tıklama aynı sipariş ve sağlayıcı oturumunu döndürür");
    Check((await Send("GET", $"/api/tickets/orders/{order.Id}", token2)).StatusCode == HttpStatusCode.NotFound, "Başka yolcu siparişi göremiyor");
    var forged = await Send("POST", "/api/payments/notifications", null, new { orderId = order.Id, eventId = "fake", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
    Check(forged.StatusCode == HttpStatusCode.BadRequest, "İmzasız sağlayıcı bildirimi reddedildi");
    var paid = await Complete(order.Id, "Succeeded");
    Check(paid.Pnr?.Length == 32 && paid.Kind == "Ticket" && paid.PaymentStatus == "Paid" && paid.IsTest, "Doğrulanmış test ödemesinden sonra PNR düzenlendi");
    // Exercise the full 32-character identifier width without changing the production PNR generator.
    await db.Reservations.Where(r => r.Pnr == paid.Pnr).ExecuteUpdateAsync(s => s.SetProperty(r => r.Pnr, new string('F', 32)));
    paid = await Get(order.Id);
    var detailResponse = await Send("GET", $"/api/tickets/{paid.Pnr}", token1);
    var ticketDetail = await Read(detailResponse);
    Check(ticketDetail.PassengerName == u1.FullName && ticketDetail.ArrivalTime > ticketDetail.DepartureTime && ticketDetail.Pnr == paid.Pnr && ticketDetail.CanCancel && ticketDetail.CancelDeadline == ticketDetail.DepartureTime.AddHours(-2), "Aktif bilet detayında yolcu, varış, PNR ve iki saat iptal son zamanı doğru");
    Check(detailResponse.Headers.CacheControl?.NoStore == true && detailResponse.Headers.CacheControl.Private, "Kişisel bilet detayı private/no-store ile sunuluyor");
    Check((await Send("GET", $"/api/tickets/{paid.Pnr}", token2)).StatusCode == HttpStatusCode.NotFound &&
        (await Send("GET", $"/api/tickets/{paid.Pnr}/pdf", token2)).StatusCode == HttpStatusCode.NotFound, "Başka yolcu bilet detayına ve PDF'sine erişemiyor");
    Check((await Send("GET", $"/api/tickets/{paid.Pnr}/pdf", null)).StatusCode == HttpStatusCode.Unauthorized &&
        (await Send("GET", $"/api/tickets/{paid.Pnr}", null)).StatusCode == HttpStatusCode.Unauthorized, "PNR herkese açık erişim anahtarı değil; girişsiz detay ve PDF 401");
    Check((await Send("GET", $"/api/tickets/{paid.Pnr}/pdf", companyToken)).StatusCode == HttpStatusCode.Forbidden, "Firma hesabı yolcuya özel PDF endpoint'ine erişemiyor");
    await DownloadPdf(paid.Pnr!, "ticket-active-test.pdf");
    await SaveSnapshot(ticketDetail, "active");
    var duplicates = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Complete(order.Id, "Succeeded")));
    Check(duplicates.All(p => p.Pnr == paid.Pnr) && await db.PaymentNotifications.CountAsync(n => n.OrderId == order.Id) == 1, "Tekrarlanan ödeme bildirimleri tek PNR ve tek bildirim kaydı oluşturdu");
    var failedOrder = await Start(2);
    var failed = await Complete(failedOrder.Id, "Failed");
    Check(failed.Pnr == null && failed.PaymentStatus == "Failed" && failed.TicketStatus == "Cancelled", "Başarısız ödemede bilet yok ve koltuk serbest");
    Check((await Start(2)).Id != failed.Id, "Başarısız ödeme koltuğu yeni satın almaya açık");
    var sameSeat = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Send("POST", "/api/tickets/checkout", token1, new { tripId = trip.Id, seatNumber = 3, requestKey = Guid.NewGuid(), amount = 1 })));
    Check(sameSeat.Count(r => r.IsSuccessStatusCode) == 1 && sameSeat.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 7, "Aynı koltuğa 8 eş zamanlı satın alma: tek tutma");
    var repeatedKey = Guid.NewGuid();
    var clicks = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Start(12, repeatedKey)));
    Check(clicks.Select(p => p.Id).Distinct().Count() == 1 && await db.LocalPaymentRecords.CountAsync(p => p.Id == clicks[0].Id) == 1, "Aynı işlem anahtarıyla eş zamanlı tıklamalar tek sağlayıcı oturumu oluşturdu");
    var abandoned = await Start(4);
    await db.Reservations.Where(r => r.Payment!.Id == abandoned.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.HoldExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
    await Service(s => s.ExpireHolds(default));
    Check(!await db.Reservations.AnyAsync(r => r.Payment!.Id == abandoned.Id && r.Status == "Active") && !await db.Reservations.AnyAsync(r => r.Payment!.Id == abandoned.Id && r.Pnr != null), "Yarıda bırakılan ödeme tutması süresi dolunca serbest; bilet yok");
    var late = await Complete(abandoned.Id, "Succeeded");
    Check(late.Pnr == null && late.PaymentStatus == "Paid" && late.RefundStatus == "Pending" && late.NeedsAttention, "Geç gelen başarılı ödeme bilet üretmedi; telafi tam iadesi kaydedildi");
    await Service(s => s.ProcessRefund(late.Id, default));
    Check((await Get(late.Id)).RefundStatus == "Succeeded", "Bilet düzenlenemeyen ödemenin telafi iadesi doğrulandı");
    // Recovery after the provider captured payment but notification delivery was lost.
    var missed = await Start(5);
    await db.LocalPaymentRecords.Where(p => p.Id == missed.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Succeeded"));
    await Service(s => s.Reconcile(missed.Id, default));
    Check((await Get(missed.Id)).Pnr != null, "Kayıp ödeme bildirimi sunucudan sağlayıcı mutabakatıyla kurtarıldı");
    var cutoff = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(1).Date, DateTimeKind.Utc);
    foreach (var minutes in new[] { 121, 120, 119 })
    {
        var boundary = await Start(6 + 121 - minutes); boundary = await Complete(boundary.Id, "Succeeded");
        await db.Trips.Where(t => t.Id == trip.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.DepartureTime, cutoff.AddMinutes(minutes)));
        var accepted = true;
        try { await Service(s => s.CancelTicket(u1.Id, boundary.Pnr!, default), new FixedClock(cutoff)); }
        catch (PaymentFlowException) { accepted = false; }
        Check(accepted == (minutes >= 120), $"Sunucu UTC saatiyle iptal sınırı: kalkışa {minutes} dakika");
    }
    // Restore future departure for the remaining test scenarios.
    await db.Trips.Where(t => t.Id == trip.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.DepartureTime, trip.DepartureTime));
    var cancels = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Send("POST", $"/api/tickets/{paid.Pnr}/cancel", token1)));
    Check(cancels.All(r => r.IsSuccessStatusCode), "8 eş zamanlı iptal isteği idempotent");
    var pendingRefund = await Get(order.Id);
    Check(pendingRefund.TicketStatus == "Cancelled" && pendingRefund.RefundStatus == "Pending" && pendingRefund.RefundAmount == 500, "Bilet iptali ve bekleyen tam iade ayrı durumlar");
    var cancelledDetail = await Read(await Send("GET", $"/api/tickets/{paid.Pnr}", token1));
    Check(cancelledDetail.TicketStatus == "Cancelled" && cancelledDetail.CancelledAt != null && cancelledDetail.RefundStatus == "Pending" && !cancelledDetail.CanCancel, "İptal edilmiş bilet detayı güncel iptal zamanı ve bekleyen iade durumunu sunuyor");
    await DownloadPdf(paid.Pnr!, "ticket-cancelled-test.pdf");
    Check(cancelledDetail.RefundDescription.Contains("İade talebi kaydedildi, sonuç bekleniyor") && !cancelledDetail.RefundDescription.Contains("Test iadesi:"), "Bekleyen test iadesi sayfa/PDF için tek backend açıklaması kullanıyor");
    await SaveSnapshot(cancelledDetail, "pending");
    await DownloadPdf(paid.Pnr!, "ticket-refund-pending.pdf", true);
    Check((await Send("POST", $"/api/tickets/{paid.Pnr}/cancel", token2)).StatusCode == HttpStatusCode.NotFound, "Başka yolcunun bilet iptali engellendi");
    var waiting = await Refund(order.Id, "Pending");
    Check(waiting.RefundStatus == "Pending" && !await db.PaymentOrders.AnyAsync(p => p.Id == order.Id && p.RefundedAt != null), "Bekleyen sağlayıcı yanıtı iade tamamlandı sayılmadı");
    var refundFailed = await Refund(order.Id, "Failed");
    Check(refundFailed.RefundStatus == "Failed" && refundFailed.LastError != null, "Başarısız iade kaydı ve hata bilgisi tutuluyor");
    Check(refundFailed.RefundDescription.Contains(refundFailed.LastError!) && !refundFailed.RefundDescription.Contains("Tam iade tamamlandı"), "Başarısız iade açıklaması gerçek hata bilgisini içeriyor; tamamlandı demiyor");
    Check(refundFailed.CancellationReason == cancelledDetail.CancellationReason && refundFailed.CancelledAt == cancelledDetail.CancelledAt, "İade hatası iptal nedeni ve tarihini değiştirmedi");
    await SaveSnapshot(refundFailed, "failed");
    await DownloadPdf(paid.Pnr!, "ticket-refund-failed.pdf", true);
    var refundSuccess = await Refund(order.Id, "Succeeded");
    Check(refundSuccess.RefundStatus == "Succeeded" && await db.PaymentOrders.AnyAsync(p => p.Id == order.Id && p.RefundedAt != null), "Başarılı sağlayıcı sonucu iade tamamlandı durumuna geçiriyor");
    Check(refundSuccess.RefundStatusLabel == "Tam iade tamamlandı" && refundSuccess.RefundDescription.Contains("Tam iade tamamlandı") && !refundSuccess.RefundDescription.Contains("sonuç bekleniyor"), "Tamamlanan iadede güncel açıklama bekleyen metni yerine tam iadeyi gösteriyor");
    Check(refundSuccess.CancellationReason == cancelledDetail.CancellationReason && refundSuccess.CancelledAt == cancelledDetail.CancelledAt, "İade tamamlanması geçmiş iptal nedenini ve zamanını değiştirmedi");
    await SaveSnapshot(refundSuccess, "cancelled");
    Check((await Read(await Send("GET", $"/api/tickets/{paid.Pnr}", token1))).RefundStatus == "Succeeded", "Aynı bilet detay isteği eski yanıt yerine güncellenmiş iade sonucunu getiriyor");
    await DownloadPdf(paid.Pnr!, "ticket-cancelled-test.pdf");
    await Refund(order.Id, "Succeeded"); await Send("POST", $"/api/tickets/{paid.Pnr}/cancel", token1);
    Check(await db.LocalPaymentRecords.Where(p => p.Id == order.Id).Select(p => p.RefundTransfers).SingleAsync() == 1, "Tekrar iptal ve tekrar iade bildirimi ikinci iade oluşturmadı");
    var retryOrder = await Start(11);
    retryOrder = await Read(await Send("POST", $"/api/tickets/local/{retryOrder.Id}/complete", token1, new { outcome = "Succeeded", refundOutcome = "Failed" }));
    await Send("POST", $"/api/tickets/{retryOrder.Pnr}/cancel", token1);
    await Service(s => s.ProcessRefund(retryOrder.Id, default));
    Check(await db.PaymentOrders.AnyAsync(p => p.Id == retryOrder.Id && p.RefundStatus == "Failed" && p.RefundAttempts == 1 && p.NextRefundAttemptAt != null), "İade hatası kalıcı kaydedildi ve artan beklemeli yeniden deneme planlandı");
    var retryKey = await db.PaymentOrders.Where(p => p.Id == retryOrder.Id).Select(p => p.RefundKey).SingleAsync();
    await db.LocalPaymentRecords.Where(p => p.Id == retryOrder.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.RefundOutcome, "Succeeded"));
    await db.PaymentOrders.Where(p => p.Id == retryOrder.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.NextRefundAttemptAt, DateTime.UtcNow.AddSeconds(-1)));
    await Task.WhenAll(Service(s => s.ProcessRefund(retryOrder.Id, default)), Service(s => s.ProcessRefund(retryOrder.Id, default)));
    Check(await db.PaymentOrders.AnyAsync(p => p.Id == retryOrder.Id && p.RefundStatus == "Succeeded" && p.RefundKey == retryKey) &&
        await db.LocalPaymentRecords.Where(p => p.Id == retryOrder.Id).Select(p => p.RefundTransfers).SingleAsync() == 1, "Eş zamanlı iade çalışanları aynı anahtarla güvenli yeniden denedi, tek transfer oluştu");
    await db.PaymentOrders.Where(p => p.Id == retryOrder.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.RefundStatus, "Pending").SetProperty(p => p.RefundedAt, (DateTime?)null).SetProperty(p => p.NextRefundAttemptAt, DateTime.UtcNow.AddSeconds(-1)));
    await Service(s => s.ProcessRefund(retryOrder.Id, default));
    Check((await Get(retryOrder.Id)).RefundStatus == "Succeeded" && await db.LocalPaymentRecords.Where(p => p.Id == retryOrder.Id).Select(p => p.RefundTransfers).SingleAsync() == 1, "İade sonrası kayıp uygulama yanıtı aynı anahtarla mutabakat edildi, çift iade yok");
    var companyRows = await (await Send("GET", "/api/reservations/company", companyToken)).Content.ReadFromJsonAsync<JsonElement>();
    Check(companyRows.GetProperty("items").EnumerateArray().Any(p => p.GetProperty("pnr").ValueKind == JsonValueKind.String && p.GetProperty("paymentStatus").GetString() == "Paid"), "Firma paneli PNR, ödeme ve iade durumlarını görebiliyor");
    var otherRows = await (await Send("GET", "/api/reservations/company", otherToken)).Content.ReadFromJsonAsync<JsonElement>();
    Check(otherRows.GetProperty("totalCount").GetInt32() == 0, "Başka firmanın bilet ve ödeme bilgileri görünmüyor");
    var activeTicket = await Start(10); activeTicket = await Complete(activeTicket.Id, "Succeeded");
    await db.Trips.Where(t => t.Id == trip.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.DepartureTime, DateTime.UtcNow.AddMinutes(90)));
    Check((await Send("POST", $"/api/tickets/{activeTicket.Pnr}/cancel", token1)).StatusCode == HttpStatusCode.Conflict, "Kalkışa 90 dakika kala yolcu iptali reddedildi");
    var cancelledTrip = await Send("POST", $"/api/trips/{trip.Id}/cancel", adminToken, new { reason = "Firma test iptali" });
    Check(cancelledTrip.IsSuccessStatusCode, "Firma sefer iptali başarılı");
    var forcedRefund = await Get(activeTicket.Id);
    Check(forcedRefund.TicketStatus == "Cancelled" && forcedRefund.RefundStatus == "Pending" && forcedRefund.RefundAmount == 500 && forcedRefund.CancellationReason!.Contains("Firma test iptali"), "Sefer iptali satılmış bileti iptal edip tam iade süreci başlattı");
    await Service(s => s.ProcessRefund(activeTicket.Id, default));
    Check((await Get(activeTicket.Id)).RefundStatus == "Succeeded", "Firma iptalinin iadesi sağlayıcıdan doğrulandı");
    Check(await db.Trips.AsNoTracking().Where(t => t.Id == trip.Id).Select(t => t.AvailableSeats).SingleAsync() == 20, "Tüm sefer iptallerinden sonra kontenjan tam ve eski ücretsiz kayıt geçmişte korunuyor");
    Console.WriteLine($"SONUÇ: {passed} ödeme/bilet/iade kontrolü başarılı.");

    async Task<string> Login(User u)
    {
        var r = await http.PostAsJsonAsync("/api/auth/login", new { email = u.Email, password = "TestPass123!" }); r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }
    async Task SaveSnapshot(PurchaseDto item, string state)
    {
        var path = Path.Combine(root, "tmp", "ticket-qa"); Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, state + ".json"), JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }
    async Task DownloadPdf(string pnr, string filename, bool temporary = false)
    {
        var response = await Send("GET", $"/api/tickets/{pnr}/pdf", token1);
        if (!response.IsSuccessStatusCode) throw new Exception($"PDF HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}\n{log}");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Check(response.Content.Headers.ContentType?.MediaType == "application/pdf" && bytes.Length > 10000 && System.Text.Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-", "Sahiplik kontrollü PDF endpoint'i geçerli PDF üretti: " + filename);
        Check(response.Headers.CacheControl?.NoStore == true && response.Content.Headers.ContentDisposition?.DispositionType == "attachment", "PDF cache edilmeden indirme eki olarak sunuluyor");
        var directory = temporary ? Path.Combine(root, "tmp", "ticket-qa") : Path.Combine(root, "output", "pdf"); Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, filename), bytes);
    }
    async Task<HttpResponseMessage> Send(string method, string path, string? token, object? body = null)
    {
        using var r = new HttpRequestMessage(new HttpMethod(method), path);
        if (token != null) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) r.Content = JsonContent.Create(body);
        return await http.SendAsync(r);
    }
    async Task<PurchaseDto> Read(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new Exception($"HTTP {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}\n{log}");
        return (await r.Content.ReadFromJsonAsync<PurchaseDto>())!;
    }
    Task<PurchaseDto> Start(int seat, Guid? key = null) => Send("POST", "/api/tickets/checkout", token1, new { tripId = trip.Id, seatNumber = seat, requestKey = key ?? Guid.NewGuid(), amount = 1 }).ContinueWith(async t => await Read(await t)).Unwrap();
    async Task<PurchaseDto> Complete(Guid id, string outcome) => await Read(await Send("POST", $"/api/tickets/local/{id}/complete", token1, new { outcome }));
    async Task<PurchaseDto> Refund(Guid id, string outcome) => await Read(await Send("POST", $"/api/tickets/local/{id}/refund-result", token1, new { outcome }));
    async Task<PurchaseDto> Get(Guid id) => await Read(await Send("GET", $"/api/tickets/orders/{id}", token1));
    async Task Service(Func<PaymentService, Task> action, TimeProvider? clock = null)
    {
        await using var context = new TravelaDbContext(options);
        var time = clock ?? TimeProvider.System;
        var provider = new LocalTestPaymentProvider(context, new LocalWebhookKey(), time);
        await action(new PaymentService(context, provider, time));
    }
}
finally
{
    if (server is { HasExited: false }) { server.Kill(true); await server.WaitForExitAsync(); }
    server?.Dispose(); SqlConnection.ClearAllPools();
    if (cs.InitialCatalog == database && database.StartsWith("TravelaPaymentTests_", StringComparison.Ordinal)) await db.Database.EnsureDeletedAsync();
}
void Check(bool ok, string message) { if (!ok) throw new Exception("BAŞARISIZ: " + message); passed++; Console.WriteLine("OK: " + message); }
static User User(string name, string role = "Passenger") { var u = new User { FullName = name, Email = name + "@travela.test", Role = role }; u.PasswordHash = new PasswordHasher<User>().HashPassword(u, "TestPass123!"); return u; }
sealed class FixedClock(DateTime utc) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc)); }
