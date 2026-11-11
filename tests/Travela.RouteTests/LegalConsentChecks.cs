using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Legal;
using Travela.Api.Models;
using Travela.Api.Payments;
using Travela.Api.Trips;
using Travela.Data;
using Travela.DTOs;
using Travela.Models;

internal static class LegalConsentChecks
{
    public static async Task Run(
        TravelaDbContext db,
        Func<string, string, User?, object?, Task<HttpResponseMessage>> send,
        User buyer,
        Action<bool, string> check)
    {
        List<LegalAcceptanceDto> Accept(params string[] types) => types.Select(type => new LegalAcceptanceDto
        {
            DocumentType = type,
            Version = LegalDocuments.Version,
            Accepted = true
        }).ToList();

        async Task<JsonElement> Json(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
                throw new Exception($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        var catalog = await Json(await send("GET", "/api/legal/documents", null, null));
        check(catalog.GetProperty("version").GetString() == LegalDocuments.Version &&
            catalog.GetProperty("notice").GetString() == "Hukuki inceleme gerektirir",
            "Hukuki belge API'si frontend ile aynı sürümlü taslağı döndürür");

        var registrationEmail = Guid.NewGuid().ToString("N") + "@consent.example";
        var password = Guid.NewGuid().ToString("N") + "Aa1!";
        object Registration(List<LegalAcceptanceDto>? acceptances) => new
        {
            fullName = "Hukuki Onay Testi",
            email = registrationEmail,
            password,
            confirmPassword = password,
            legalAcceptances = acceptances
        };
        var usersBefore = await db.Users.CountAsync();
        var invalid = new List<LegalAcceptanceDto>?[]
        {
            null,
            [],
            Accept(LegalDocuments.Privacy),
            [new() { DocumentType = LegalDocuments.Privacy, Version = LegalDocuments.Version, Accepted = false },
                .. Accept(LegalDocuments.Terms)],
            Accept(LegalDocuments.Privacy, LegalDocuments.Privacy)
        };
        foreach (var acceptances in invalid)
            check((await send("POST", "/api/auth/register", null, Registration(acceptances))).StatusCode == HttpStatusCode.BadRequest,
                "Eksik/işaretlenmemiş/tekrarlı kayıt onayı sunucuda reddedilir");
        var old = Accept(LegalDocuments.Privacy, LegalDocuments.Terms);
        old[0].Version = "eski-surum";
        check((await send("POST", "/api/auth/register", null, Registration(old))).StatusCode == HttpStatusCode.Conflict,
            "Eski belge sürümü 409 döndürür");
        check(await db.Users.CountAsync() == usersBefore && !await db.LegalAcceptances.AnyAsync(),
            "Reddedilen onayda kullanıcı ve onay kaydı oluşmaz");
        var registered = await Json(await send("POST", "/api/auth/register", null,
            Registration(Accept(LegalDocuments.Privacy, LegalDocuments.Terms))));
        var registeredId = registered.GetProperty("userId").GetInt32();
        var registrationAudits = await db.LegalAcceptances.AsNoTracking().Where(a => a.UserId == registeredId).ToListAsync();
        check(registrationAudits.Count == 2 && registrationAudits.All(a => a.Context == "Registration" &&
            a.DocumentVersion == LegalDocuments.Version && IPAddress.IsLoopback(IPAddress.Parse(a.IpAddress))),
            "Kayıt onayları doğru kullanıcı, sürüm, sunucu zamanı ve gerçek bağlantı IP'siyle saklanır");
        check(registrationAudits.All(a => a.AcceptedAt > DateTime.UtcNow.AddMinutes(-2) &&
            a.DocumentHash == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(a.DocumentSnapshot)))),
            "Saklanan belge kopyasının SHA-256 özeti doğrulanır");

        var company = new Company { Name = "Onay Test Firması" };
        var bus = new Bus { Company = company, PlateNumber = "35 CNS 001", NormalizedPlateNumber = "35CNS001", SeatCapacity = 40 };
        var route = new Travela.Api.Models.Route { DepartureCity = "İzmir", ArrivalCity = "Ankara" };
        var trip = new Trip
        {
            Company = company, Bus = bus, Route = route,
            DepartureTime = DateTime.UtcNow.AddDays(20), ArrivalTime = DateTime.UtcNow.AddDays(20).AddHours(6),
            Price = 500, AvailableSeats = 40, SeatLayout = "2+2", SeatCapacitySnapshot = 40, SellableSeatCount = 40,
            SeatPlanJson = SeatPlans.Write(SeatPlans.Create("2+2", 40))
        };
        db.Add(trip);
        var other = new User { FullName = "Diğer Test Yolcusu", Email = Guid.NewGuid().ToString("N") + "@consent.example" };
        db.Add(other);
        await db.SaveChangesAsync();

        var requestKey = Guid.NewGuid();
        object Group(List<LegalAcceptanceDto>? acceptances) => new
        {
            tripId = trip.Id,
            requestKey,
            userId = other.Id,
            companyId = -1,
            passengers = new[]
            {
                new
                {
                    seatNumber = 1, fullName = "Test Yolcusu", email = "traveler@consent.example",
                    phone = "+905321234567", gender = "Female"
                }
            },
            legalAcceptances = acceptances
        };
        var missingGroupConsent = await send("POST", "/api/purchases", buyer, Group(null));
        check(missingGroupConsent.StatusCode == HttpStatusCode.BadRequest,
            $"Ortak satın alma onaysız başlatılamaz ({missingGroupConsent.StatusCode}: {await missingGroupConsent.Content.ReadAsStringAsync()})");
        var uncheckedSales = Accept(LegalDocuments.Sales);
        uncheckedSales[0].Accepted = false;
        check((await send("POST", "/api/purchases", buyer, Group(uncheckedSales))).StatusCode == HttpStatusCode.BadRequest,
            "İşaretlenmemiş satış onayı, sürümü doğru olsa da reddedilir");
        var singleRequestKey = Guid.NewGuid();
        object Single(List<LegalAcceptanceDto>? acceptances) => new
        {
            tripId = trip.Id, seatNumber = 2, gender = "Female",
            requestKey = singleRequestKey, legalAcceptances = acceptances
        };
        check((await send("POST", "/api/tickets/checkout", buyer, Single(null))).StatusCode == HttpStatusCode.BadRequest,
            "Eski tek bilet endpoint'i onay kuralını atlayamaz");
        check(await db.Trips.Where(t => t.Id == trip.Id).Select(t => t.AvailableSeats).SingleAsync() == 40 &&
            !await db.Reservations.AnyAsync(r => r.TripId == trip.Id),
            "Onay yoksa koltuk ve ödeme siparişi oluşturulmaz");
        var obsoleteSales = Accept(LegalDocuments.Sales);
        obsoleteSales[0].Version = "eski-surum";
        check((await send("POST", "/api/purchases", buyer, Group(obsoleteSales))).StatusCode == HttpStatusCode.Conflict,
            "Satın alma eski sözleşme sürümünü reddeder");
        var group = await Json(await send("POST", "/api/purchases", buyer, Group(Accept(LegalDocuments.Sales))));
        var groupId = group.GetProperty("id").GetGuid();
        var audit = await db.LegalAcceptances.AsNoTracking().SingleAsync(a => a.PurchaseGroupId == groupId);
        check(audit.UserId == buyer.Id && audit.UserId != other.Id && audit.RequestKey == requestKey &&
            audit.DocumentType == LegalDocuments.Sales && audit.TransactionSnapshot!.Contains("500"),
            "Satın alma onayı JWT sahibine ve gerçek sipariş/sefer/tutar görüntüsüne bağlanır");
        check(group.GetProperty("legalAccepted").GetBoolean() && group.GetProperty("checkoutUrl").ValueKind == JsonValueKind.String,
            "Onay ve sipariş atomik kaydedildikten sonra ödeme bağlantısı açılır");
        await Json(await send("POST", "/api/purchases", buyer, Group(Accept(LegalDocuments.Sales))));
        check(await db.LegalAcceptances.CountAsync(a => a.PurchaseGroupId == groupId) == 1 &&
            await db.Reservations.CountAsync(r => r.TripId == trip.Id) == 1,
            "Aynı işlem anahtarıyla tekrar onay ve koltuk çoğaltılmaz");
        var single = await Json(await send("POST", "/api/tickets/checkout", buyer, Single(Accept(LegalDocuments.Sales))));
        var singleId = single.GetProperty("id").GetGuid();
        check(await db.LegalAcceptances.AnyAsync(a => a.PaymentOrderId == singleId && a.UserId == buyer.Id),
            "Tek bilet akışında da onay siparişle aynı işlemde kaydedilir");

        // Reproduce a pre-migration pending order without inventing historical acknowledgement.
        var legacyReservation = new Reservation
        {
            Trip = trip, PassengerId = buyer.Id, SeatNumber = 3, Price = 500,
            Kind = "Hold", HoldExpiresAt = DateTime.UtcNow.AddMinutes(5),
            PassengerNameSnapshot = "Eski Yolcu", PassengerGender = "Female"
        };
        var legacyOrder = new PaymentOrder
        {
            PassengerId = buyer.Id, Reservation = legacyReservation, Amount = 500, OriginalAmount = 500,
            RequestKey = Guid.NewGuid()
        };
        db.Add(legacyOrder);
        await db.SaveChangesAsync();
        var localProvider = new LocalTestPaymentProvider(db, new LocalWebhookKey(), TimeProvider.System);
        await new PaymentService(db, localProvider, TimeProvider.System).Reconcile(legacyOrder.Id, default);
        check(!await db.LocalPaymentRecords.AnyAsync(p => p.Id == legacyOrder.Id),
            "Arka plan sorgulaması da onaysız sipariş için ödeme oturumu açmaz");
        var oldDetail = await Json(await send("GET", $"/api/tickets/orders/{legacyOrder.Id}", buyer, null));
        check(!oldDetail.GetProperty("legalAccepted").GetBoolean() && oldDetail.GetProperty("checkoutUrl").ValueKind == JsonValueKind.Null,
            "Eski onaysız bekleyen siparişte ödeme bağlantısı sunulmaz");
        check((await send("POST", $"/api/tickets/local/{legacyOrder.Id}/complete", buyer, new { outcome = "Succeeded" })).StatusCode == HttpStatusCode.BadRequest,
            "Ödeme kontrol endpoint'i onaysız eski siparişi de reddeder");
        var adoption = new { legalAcceptances = Accept(LegalDocuments.Sales) };
        check((await send("POST", $"/api/tickets/orders/{legacyOrder.Id}/legal-acceptance", other, adoption)).StatusCode == HttpStatusCode.NotFound,
            "Başka yolcu siparişe onay ekleyemez");
        await Json(await send("POST", $"/api/tickets/orders/{legacyOrder.Id}/legal-acceptance", buyer, adoption));
        await Json(await send("POST", $"/api/tickets/orders/{legacyOrder.Id}/legal-acceptance", buyer, adoption));
        check(await db.LegalAcceptances.CountAsync(a => a.PaymentOrderId == legacyOrder.Id) == 1,
            "Eski siparişe yalnızca şimdiki açık onay kaydedilir ve tekrar çoğaltılmaz");

        var now = DateTime.UtcNow;
        check(PaymentService.CanCancel(now.AddHours(2), now) &&
            !PaymentService.CanCancel(now.AddHours(2).AddTicks(-1), now),
            "Tam iki saatte iade hakkı var; sınırın altında yok");
    }
}
