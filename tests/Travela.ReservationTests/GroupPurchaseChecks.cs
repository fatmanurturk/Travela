using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.Api.Trips;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

internal static class GroupPurchaseChecks
{
    public static async Task Run(TravelaDbContext db, HttpClient http, Action<bool, string> check, string root)
    {
        var company = new Company { Name = "Ortak Sipariş Firması" }; var route = new Travela.Api.Models.Route { DepartureCity = "İzmir", ArrivalCity = "Ankara" };
        var bus = new Bus { Company = company, PlateNumber = "35 GROUP 1", NormalizedPlateNumber = "35GROUP1", SeatCapacity = 40, SeatLayout = "2+2", SeatPlanJson = SeatPlans.Write(SeatPlans.Create("2+2", 40, [40])) };
        var trip = new Trip { Company = company, Bus = bus, Route = route, Price = 333.33m, AvailableSeats = 39, SellableSeatCount = 39, SeatCapacitySnapshot = 40, SeatLayout = "2+2", SeatPlanJson = bus.SeatPlanJson, DepartureTime = DateTime.UtcNow.AddDays(20), ArrivalTime = DateTime.UtcNow.AddDays(20).AddHours(8) };
        var buyer = new User { FullName = "Hesap Sahibi", Email = "buyer@group.example", Role = "Passenger" }; buyer.PasswordHash = new PasswordHasher<User>().HashPassword(buyer, "TestPass123!");
        var stranger = new User { FullName = "Diğer Hesap", Email = "stranger@group.example", Role = "Passenger" }; stranger.PasswordHash = new PasswordHasher<User>().HashPassword(stranger, "TestPass123!");
        var admin = new User { FullName = "Sipariş Yöneticisi", Email = "admin@group.example", Role = "Admin" }; admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, "TestPass123!");
        db.AddRange(trip, buyer, stranger, admin); await db.SaveChangesAsync();
        async Task<HttpResponseMessage> Send(string method, string path, string? token, object? body = null) { using var request = new HttpRequestMessage(new HttpMethod(method), path); if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); if (body != null) request.Content = JsonContent.Create(body); return await http.SendAsync(request); }
        async Task<JsonElement> Json(HttpResponseMessage response) { if (!response.IsSuccessStatusCode) throw new Exception(response.StatusCode + ": " + await response.Content.ReadAsStringAsync()); return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone(); }
        async Task<string> Login(User user) => (await Json(await Send("POST", "/api/auth/login", null, new { email = user.Email, password = "TestPass123!" }))).GetProperty("token").GetString()!;
        var token = await Login(buyer); var otherToken = await Login(stranger);
        TicketPassengerDto Passenger(int seat, string name = "Başka Yolcu") => new() { SeatNumber = seat, FullName = name, Email = "traveler" + seat + "@group.example", Phone = "+905321234567" };
        StartGroupPurchaseDto Request(params int[] seats) => new() { TripId = trip.Id, RequestKey = Guid.NewGuid(), Passengers = seats.Select(n => Passenger(n)).ToList() };
        var dto = Request(1, 2); dto.Passengers[1].FullName = "İkinci Yolcu";
        var response = await Send("POST", "/api/purchases", token, dto); var group = await Json(response); var id = group.GetProperty("id").GetGuid();
        check(group.GetProperty("amount").GetDecimal() == 666.66m && group.GetProperty("tickets").GetArrayLength() == 2 && group.GetProperty("tickets")[0].GetProperty("pnr").ValueKind == JsonValueKind.Null, "İki koltuk tek siparişte sunucu fiyatıyla ayrılır; ödeme öncesi PNR oluşmaz");
        var again = await Json(await Send("POST", "/api/purchases", token, dto)); check(again.GetProperty("id").GetGuid() == id && await db.PurchaseGroups.CountAsync(g => g.Id == id) == 1, "Aynı ortak sipariş anahtarı ikinci sipariş veya tutma oluşturmaz");
        dto.Passengers[0].FullName = "Değiştirilmiş Yolcu"; check((await Send("POST", "/api/purchases", token, dto)).StatusCode == HttpStatusCode.Conflict, "Aynı anahtarla yolcu bilgisini değiştirme engellenir");
        check((await Send("GET", $"/api/purchases/{id}", otherToken)).StatusCode == HttpStatusCode.NotFound && (await Send("POST", "/api/purchases", null, Request(4))).StatusCode == HttpStatusCode.Unauthorized, "Ortak sipariş kimlik ve sahiplik denetimleriyle korunur");
        var before = await db.Trips.Where(t => t.Id == trip.Id).Select(t => t.AvailableSeats).SingleAsync();
        check((await Send("POST", "/api/purchases", token, Request(2, 3))).StatusCode == HttpStatusCode.Conflict && !await db.Reservations.AnyAsync(r => r.TripId == trip.Id && r.SeatNumber == 3 && r.Status == "Active") && await db.Trips.AnyAsync(t => t.Id == trip.Id && t.AvailableSeats == before), "Bir koltuk doluysa ortak sipariş bütünüyle reddedilir; diğer koltuk tutulmaz");
        check((await Send("POST", "/api/purchases", token, Request(4, 4))).StatusCode == HttpStatusCode.BadRequest && (await Send("POST", "/api/purchases", token, Request(40))).StatusCode == HttpStatusCode.Conflict && (await Send("POST", "/api/purchases", token, Request(Enumerable.Range(4,11).ToArray()))).StatusCode == HttpStatusCode.BadRequest, "Tekrarlı, kapalı koltuk ve on yolcu üzeri sipariş reddedilir");
        group = await Json(await Send("POST", $"/api/purchases/{id}/local-complete", token, new { outcome = "Succeeded" }));
        var tickets = group.GetProperty("tickets").EnumerateArray().ToList(); var pnr = tickets[0].GetProperty("pnr").GetString()!; var lineId = tickets[0].GetProperty("id").GetGuid();
        check(tickets.All(t => t.GetProperty("kind").GetString() == "Ticket") && tickets.Select(t => t.GetProperty("pnr").GetString()).Distinct().Count() == 2 && tickets[0].GetProperty("passengerName").GetString() == "Başka Yolcu" && tickets[1].GetProperty("passengerName").GetString() == "İkinci Yolcu", "Tek ödeme her yolcu için ayrı PNR ve başkası adına değişmeyen yolcu bilgisi üretir");
        await Json(await Send("POST", $"/api/purchases/{id}/local-complete", token, new { outcome = "Succeeded" }));
        check(await db.Reservations.CountAsync(r => r.Payment!.PurchaseGroupId == id && r.Kind == "Ticket") == 2, "Yinelenen ortak ödeme sonucu bilet sayısını artırmaz");
        await Json(await Send("POST", $"/api/tickets/{pnr}/cancel", token)); await Json(await Send("POST", $"/api/tickets/local/{lineId}/refund-result", token, new { outcome = "Succeeded" }));
        group = await Json(await Send("GET", $"/api/purchases/{id}", token));
        check(group.GetProperty("refunded").GetDecimal() == 333.33m && group.GetProperty("tickets").EnumerateArray().Count(t => t.GetProperty("ticketStatus").GetString() == "Active") == 1, "Tek bilet iptali yalnızca kendi tutarını iade eder; diğer bilet aktiftir");
        check((await Send("POST", $"/api/tickets/local/{tickets[1].GetProperty("id").GetGuid()}/complete", token, new { outcome = "Succeeded" })).StatusCode == HttpStatusCode.Conflict, "Ortak sipariş satırı ayrı ödeme gibi simüle edilemez");
        var lateGroup = await Json(await Send("POST", "/api/purchases", token, Request(5, 6))); var lateId = lateGroup.GetProperty("id").GetGuid();
        await db.Reservations.Where(r => r.Payment!.PurchaseGroupId == lateId && r.SeatNumber == 5).ExecuteUpdateAsync(s => s.SetProperty(r => r.HoldExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        lateGroup = await Json(await Send("POST", $"/api/purchases/{lateId}/local-complete", token, new { outcome = "Succeeded" }));
        check(lateGroup.GetProperty("tickets").EnumerateArray().All(t => t.GetProperty("pnr").ValueKind == JsonValueKind.Null && t.GetProperty("refundStatus").GetString() == "Pending"), "Ödeme anında tek tutma bile geçersizse hiçbir bilet düzenlenmez; tüm tutar telafi iadesine girer");
        // Concurrent overlapping carts reserve their full seat set or none.
        var races = await Task.WhenAll(Send("POST", "/api/purchases", token, Request(7,8)), Send("POST", "/api/purchases", otherToken, Request(8,9)));
        check(races.Count(r => r.IsSuccessStatusCode) == 1 && races.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 1 && await db.Reservations.CountAsync(r => r.TripId == trip.Id && r.Status == "Active" && r.SeatNumber >= 7 && r.SeatNumber <= 9) == 2, "Çakışan iki ortak siparişte yalnızca bir sepet bütünüyle ayrılır");
        var queued = await Json(await Send("POST", "/api/purchases", token, Request(16,17))); var queuedId = queued.GetProperty("id").GetGuid(); var adminToken = await Login(admin);
        var central = await Json(await Send("GET", "/api/admin/tickets?search=" + queuedId, adminToken));
        check(central.GetProperty("totalCount").GetInt32() == 2, "Yönetici ortak sipariş kimliğiyle tüm bilet satırlarını merkezi aramada bulur");
        await db.PurchaseGroups.Where(g => g.Id == queuedId).ExecuteUpdateAsync(s => s.SetProperty(g => g.NextReconciliationAt, DateTime.UtcNow.AddDays(2)));
        var queuedReservation = await db.Reservations.Where(r => r.Payment!.PurchaseGroupId == queuedId).Select(r => r.Id).FirstAsync();
        var requeued = await Send("POST", $"/api/admin/tickets/{queuedReservation}/reconcile", adminToken, new { reason = "Ortak sipariş sağlayıcı sonucu kontrol edilecek", requestKey = Guid.NewGuid() });
        check(requeued.StatusCode == HttpStatusCode.Accepted && await db.PurchaseGroups.AnyAsync(g => g.Id == queuedId && g.NextReconciliationAt <= DateTime.UtcNow), "Yönetici satır üzerinden sorgulama istediğinde ortak ödeme kuyruğu da öne alınır");
        await IyzicoChecks(db, http, buyer, trip, Passenger, check, root);
        await RecoveryQueueCheck(db, buyer, trip, Passenger, check);
    }

    private static async Task IyzicoChecks(TravelaDbContext db, HttpClient http, User buyer, Trip trip, Func<int,string,TicketPassengerDto> passenger, Action<bool,string> check, string root)
    {
        db.ChangeTracker.Clear(); var directory = new DirectoryInfo(Path.Combine(root, "tmp", "iyzico-qa", "keys")); directory.Create(); var protection = DataProtectionProvider.Create(directory);
        var options = Options.Create(new IyzicoOptions { ApiKey = "sandbox-test-key", SecretKey = "sandbox-test-secret", CallbackBaseUrl = "https://example.test" });
        var fake = new FakeIyzico(db, options.Value.SecretKey); var provider = new IyzicoSandboxProvider(new HttpClient(fake), db, options, protection); var service = new PaymentService(db, provider, TimeProvider.System);
        var billing = new CheckoutBuyerDto { Name = "Alıcı", Surname = "Test", IdentityNumber = "11111111111", Address = "Sandbox test adresi", City = "İzmir", Country = "Türkiye", ZipCode = "35000", Phone = "+905321234567" };
        var protectedBuyer = protection.CreateProtector("Travela.IyzicoBuyer.v1").Protect(JsonSerializer.Serialize(new IyzicoBuyer(billing, buyer.Email, "127.0.0.1"), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var request = new StartGroupPurchaseDto { TripId = trip.Id, RequestKey = Guid.NewGuid(), Buyer = billing, Passengers = [passenger(11, "Sandbox Bir"), passenger(12, "Sandbox İki")] };
        var group = await service.StartGroup(buyer.Id, request, protectedBuyer, default); var id = group.Id;
        check(group.CheckoutState == "Ready" && IyzicoSandboxProvider.AllowedCheckoutUrl(group.CheckoutUrl) && fake.Initializations == 1 && fake.AuthValid && group.ProtectedBuyerJson == null, "iyzico sandbox başlatma imzalı istek, iki sepet satırı ve güvenli hosted URL kullanır; alıcı kimliği işlemden sonra tutulmaz");
        check(!IyzicoSandboxProvider.AllowedCheckoutUrl("https://sandbox-cpp.iyzipay.com.evil.test/?token=x") && !IyzicoSandboxProvider.AllowedCheckoutUrl("http://sandbox-cpp.iyzipay.com/?token=x"), "Sahte sağlayıcı alan adı ve HTTP ödeme adresi reddedilir");
        fake.TimeoutQuery = true;
        try { await service.ReconcileGroup(id, default); throw new Exception("Zaman aşımı yutuldu"); } catch (PaymentFlowException) { check(await db.PurchaseGroups.AnyAsync(g => g.Id == id && g.PaymentStatus == "Pending" && g.NextReconciliationAt > DateTime.UtcNow), "HTTP sağlayıcı zaman aşımı worker kapanmasına veya sahte sonuca dönüşmez; ileri sorgulama planlanır"); }
        fake.TimeoutQuery = false;
        fake.BadSignature = true;
        try { await service.ReconcileGroup(id, default); throw new Exception("Bozuk imza kabul edildi"); } catch (PaymentFlowException) { check(!await db.Reservations.AnyAsync(r => r.Payment!.PurchaseGroupId == id && r.Pnr != null), "Bozuk iyzico yanıt imzası hiçbir bilet oluşturmaz"); }
        fake.BadSignature = false; fake.Fraud = 0; await service.ReconcileGroup(id, default);
        check(await db.PurchaseGroups.AnyAsync(g => g.Id == id && g.PaymentStatus == "Pending"), "iyzico fraud incelemesindeki ödeme başarılı bilete dönüştürülmez");
        fake.Fraud = 1; fake.BadLine = true;
        try { await service.ReconcileGroup(id, default); throw new Exception("Yanlış satır kabul edildi"); } catch (PaymentFlowException) { check(!await db.Reservations.AnyAsync(r => r.Payment!.PurchaseGroupId == id && r.Pnr != null), "Tutar doğru olsa da yanlış sepet satırı bilet düzenlemez"); }
        fake.BadLine = false; await service.ReconcileGroup(id, default); await service.ReconcileGroup(id, default); db.ChangeTracker.Clear();
        var lines = await db.PaymentOrders.Include(p => p.Reservation).Where(p => p.PurchaseGroupId == id).OrderBy(p => p.Id).ToListAsync();
        check(lines.All(p => p.PaymentStatus == "Paid" && p.Reservation.Kind == "Ticket") && fake.AuthValid, "iyzico sorgusunun imza, toplam ve satır kontrollerinden sonra biletler bir kez düzenlenir");
        var notice = new IyzicoNotice { IyziEventType = "CHECKOUT_FORM_AUTH", IyziPaymentId = JsonSerializer.SerializeToElement("PAY-" + id.ToString("N")), PaymentConversationId = id.ToString("N"), Token = group.ProviderToken!, Status = "SUCCESS" };
        var signature = fake.Sign(options.Value.SecretKey + notice.IyziEventType + notice.IyziPaymentId + notice.Token + notice.PaymentConversationId + notice.Status);
        check(provider.VerifyWebhook(notice, signature) && !provider.VerifyWebhook(notice, new string('0',64)), "iyzico HPP webhook V3 imzası doğrulanır; sahte bildirim reddedilir");
        check((await http.PostAsJsonAsync("/api/payments/iyzico/webhook", notice)).StatusCode == HttpStatusCode.BadRequest, "İmzasız gerçek webhook endpoint'i reddedilir");
        using (var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/payments/iyzico/webhook") { Content = JsonContent.Create(notice) })
        {
            webhook.Headers.Add("X-IYZ-SIGNATURE-V3", signature); var response = await http.SendAsync(webhook);
            check(response.IsSuccessStatusCode && await db.Reservations.CountAsync(r => r.Payment!.PurchaseGroupId == id && r.Kind == "Ticket") == 2, "Doğrulanmış iyzico webhook tekrarında HTTP 200 alınır; bilet çoğalmaz");
        }
        using (var callback = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = http.BaseAddress })
        {
            var response = await callback.PostAsync($"/api/payments/iyzico/callback/{id}", new FormUrlEncodedContent(new Dictionary<string,string> { ["token"] = group.ProviderToken! }));
            var invalid = await callback.PostAsync($"/api/payments/iyzico/callback/{id}", new FormUrlEncodedContent(new Dictionary<string,string> { ["token"] = "invalid-token" }));
            check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.AbsolutePath == "/purchase/" + id && invalid.StatusCode == HttpStatusCode.BadRequest, "iyzico form dönüşü kayıtlı token'ı doğrular ve sabit ön yüz sipariş adresine yönlendirir");
        }
        var line = lines[0]; await service.CancelTicket(buyer.Id, line.Reservation.Pnr!, default); fake.LoseRefundResponse = true; await service.ProcessRefund(line.Id, default); db.ChangeTracker.Clear();
        check(await db.PaymentOrders.AnyAsync(p => p.Id == line.Id && p.RefundStatus == "Pending") && fake.RefundPosts == 1, "iyzico iade yanıtı kaybolduğunda iade tamamlandı sayılmaz; sağlayıcı işlem izi saklanır");
        await db.PaymentOrders.Where(p => p.Id == line.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.NextRefundAttemptAt, DateTime.UtcNow.AddSeconds(-1)));
        await service.ProcessRefund(line.Id, default); db.ChangeTracker.Clear();
        check(await db.PaymentOrders.AnyAsync(p => p.Id == line.Id && p.RefundStatus == "Succeeded") && fake.RefundPosts == 1 && await db.Reservations.AnyAsync(r => r.Id == lines[1].ReservationId && r.Status == "Active"), "iyzico raporundan iade doğrulanır; ikinci para transferi veya diğer bilete iptal uygulanmaz");
        var unresolvedRequest = new StartGroupPurchaseDto { TripId = trip.Id, RequestKey = Guid.NewGuid(), Buyer = billing, Passengers = [passenger(15, "Başlatma Testi")] }; fake.LoseInitializeResponse = true;
        var unresolved = await service.StartGroup(buyer.Id, unresolvedRequest, protectedBuyer, default); var initCount = fake.Initializations;
        await service.StartGroup(buyer.Id, unresolvedRequest, protectedBuyer, default); await service.ReconcileGroup(unresolved.Id, default);
        check(fake.Initializations == initCount && (await db.PurchaseGroups.AsNoTracking().SingleAsync(g => g.Id == unresolved.Id)).CheckoutState == "Unknown", "Başlatma yanıtı kaybolan iyzico siparişi ikinci ödeme sayfası açmaz; mutabakat ve destek durumunda kalır");
        var oldDue = DateTime.UtcNow.AddDays(-3); await db.PurchaseGroups.Where(g => g.Id == unresolved.Id).ExecuteUpdateAsync(s => s.SetProperty(g => g.NextReconciliationAt, oldDue));
        await service.ReconcileGroup(unresolved.Id, default);
        check(await db.PurchaseGroups.AnyAsync(g => g.Id == unresolved.Id && g.NextReconciliationAt > DateTime.UtcNow && g.ReconciliationAttempts >= 2), "Eski sonuçlanmayan ortak ödeme her sorgudan sonra ileri planlanır; yeni siparişleri aç bırakmaz");
    }

    private static async Task RecoveryQueueCheck(TravelaDbContext db, User buyer, Trip trip, Func<int,string,TicketPassengerDto> passenger, Action<bool,string> check)
    {
        db.ChangeTracker.Clear(); var local = new LocalTestPaymentProvider(db, new LocalWebhookKey(), TimeProvider.System); var service = new PaymentService(db, local, TimeProvider.System);
        var future = DateTime.UtcNow.AddDays(2);
        await db.PaymentOrders.Where(p => p.PaymentStatus == "Pending").ExecuteUpdateAsync(s => s.SetProperty(p => p.NextReconciliationAt, future));
        await db.PurchaseGroups.Where(g => g.PaymentStatus == "Pending").ExecuteUpdateAsync(s => s.SetProperty(g => g.NextReconciliationAt, future));
        var old = await service.Start(buyer.Id, trip.Id, 21, Guid.NewGuid(), default);
        var group = await service.StartGroup(buyer.Id, new StartGroupPurchaseDto { TripId = trip.Id, RequestKey = Guid.NewGuid(), Passengers = [passenger(22,"Kuyruk Yolcusu"), passenger(23,"İkinci Kuyruk Yolcusu")] }, null, default);
        await db.LocalPaymentRecords.Where(p => p.Id == group.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Succeeded"));
        var gate = new SlowGate(old.Id); var services = new ServiceCollection();
        services.AddScoped(_ => new TravelaDbContext(new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options));
        services.AddSingleton(TimeProvider.System); services.AddSingleton<LocalWebhookKey>(); services.AddScoped<LocalTestPaymentProvider>();
        services.AddScoped<IPaymentProvider>(sp => new SlowProvider(sp.GetRequiredService<LocalTestPaymentProvider>(), gate)); services.AddScoped<PaymentService>();
        await using var container = services.BuildServiceProvider(); using var worker = new PaymentRecoveryWorker(container.GetRequiredService<IServiceScopeFactory>(), NullLogger<PaymentRecoveryWorker>.Instance);
        await worker.StartAsync(default); var completed = false;
        try
        {
            await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i=0; i<100; i++) { completed = await db.PurchaseGroups.AsNoTracking().AnyAsync(g => g.Id == group.Id && g.PaymentStatus == "Paid"); if(completed) break; await Task.Delay(30); }
        }
        finally { gate.Release.TrySetResult(true); await worker.StopAsync(default); }
        check(completed, "Eski tekil sağlayıcı sorgusu yanıt vermese de ortak sipariş kuyruğu bağımsız ilerler ve yeni biletleri düzenler");
    }
    private sealed class SlowGate(Guid id) { public Guid Id = id; public TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    private sealed class SlowProvider(LocalTestPaymentProvider inner, SlowGate gate) : IPaymentProvider
    {
        public string Name => inner.Name; public bool IsTest => true;
        public Task<string> OpenCheckout(Guid id, decimal amount, string currency, CancellationToken ct) => inner.OpenCheckout(id, amount, currency, ct);
        public async Task<ProviderPayment> GetPayment(Guid id, CancellationToken ct) { if(id==gate.Id) { gate.Started.TrySetResult(true); await gate.Release.Task.WaitAsync(ct); } return await inner.GetPayment(id, ct); }
        public Task<ProviderRefund> Refund(Guid id, decimal amount, string key, CancellationToken ct) => inner.Refund(id,amount,key,ct);
        public bool Verify(ProviderNotice notice, string signature) => inner.Verify(notice,signature);
    }
    private sealed class FakeIyzico(TravelaDbContext db, string secret) : HttpMessageHandler
    {
        public int Initializations, RefundPosts; public bool AuthValid = true, BadSignature, BadLine, LoseRefundResponse, LoseInitializeResponse, TimeoutQuery; public int Fraud = 1;
        private readonly Dictionary<string, object[]> refunds = [];
        public string Sign(string value) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(value)));
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct); var random = request.Headers.GetValues("x-iyzi-rnd").Single(); var auth = request.Headers.GetValues("Authorization").Single();
            var expected = "IYZWSv2 " + Convert.ToBase64String(Encoding.UTF8.GetBytes("apiKey:sandbox-test-key&randomKey:" + random + "&signature:" + Sign(random + request.RequestUri!.AbsolutePath + body)));
            AuthValid &= auth == expected && request.RequestUri!.Host == "sandbox-api.iyzipay.com";
            object response;
            if (request.RequestUri!.AbsolutePath.Contains("initialize"))
            {
                Initializations++; if (LoseInitializeResponse) throw new HttpRequestException("Lost init response");
                using var json = JsonDocument.Parse(body); var conversation = json.RootElement.GetProperty("conversationId").GetString()!;
                var token = "sandbox-token-" + conversation; response = new { status = "success", conversationId = conversation, token, paymentPageUrl = "https://sandbox-cpp.iyzipay.com/?token=" + token, signature = Sign(conversation + ":" + token) };
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("/detail"))
            {
                if (TimeoutQuery) throw new TaskCanceledException("HTTP timeout");
                using var json = JsonDocument.Parse(body); var conversation = json.RootElement.GetProperty("conversationId").GetString()!; var id = Guid.Parse(conversation);
                var group = await db.PurchaseGroups.AsNoTracking().Include(g => g.Items).SingleAsync(g => g.Id == id, ct); var paymentId = "PAY-" + conversation;
                var signature = Sign("SUCCESS:" + paymentId + ":TRY:" + conversation + ":" + conversation + ":" + IyzicoSandboxProvider.Money(group.Amount) + ":" + IyzicoSandboxProvider.Money(group.Amount) + ":" + group.ProviderToken);
                response = new { status = "success", paymentStatus = "SUCCESS", paymentId, currency = "TRY", basketId = conversation, conversationId = conversation, paidPrice = group.Amount, price = group.Amount, token = group.ProviderToken, fraudStatus = Fraud,
                    signature = BadSignature ? new string('0',64) : signature, itemTransactions = group.Items.Select(p => new { itemId = BadLine ? Guid.NewGuid().ToString("N") : p.Id.ToString("N"), paymentTransactionId = "TX-" + p.Id.ToString("N"), paidPrice = p.Amount }).ToArray() };
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("/details"))
            {
                var paymentId = Uri.UnescapeDataString(request.RequestUri.Query.Split("paymentId=")[1]); var id = Guid.Parse(paymentId[4..]); var group = await db.PurchaseGroups.AsNoTracking().Include(g => g.Items).SingleAsync(g => g.Id == id, ct);
                response = new { status = "success", payments = new[] { new { paymentId, paymentConversationId = id.ToString("N"), itemTransactions = group.Items.Select(p => new { paymentTransactionId = "TX-" + p.Id.ToString("N"), paidPrice = p.Amount, refunds = refunds.GetValueOrDefault("TX-" + p.Id.ToString("N"), []) }).ToArray() } } };
            }
            else
            {
                RefundPosts++; using var json = JsonDocument.Parse(body); var r = json.RootElement; var tx = r.GetProperty("paymentTransactionId").GetString()!; var key = r.GetProperty("conversationId").GetString()!; var price = decimal.Parse(r.GetProperty("price").GetString()!, System.Globalization.CultureInfo.InvariantCulture); var orderId = Guid.Parse(tx[3..]); var groupId = await db.PaymentOrders.Where(p => p.Id == orderId).Select(p => p.PurchaseGroupId!.Value).SingleAsync(ct); var paymentId = "PAY-" + groupId.ToString("N");
                refunds[tx] = [new { refundTxId = "RF-" + key, refundConversationId = key, refundPrice = price, refundStatus = 1, currencyCode = "TRY" }];
                if (LoseRefundResponse) { LoseRefundResponse = false; throw new HttpRequestException("Refund accepted, response lost"); }
                response = new { status = "success", paymentId, paymentTransactionId = tx, price, currency = "TRY", conversationId = key, signature = Sign(paymentId + ":" + IyzicoSandboxProvider.Money(price) + ":TRY:" + key) };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response, options: Options) };
        }
    }
}
