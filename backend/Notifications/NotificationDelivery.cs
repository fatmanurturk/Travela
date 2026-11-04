using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Travela.Api.Models;
using Travela.Api.Security;
using Travela.Data;

namespace Travela.Api.Notifications;

public sealed class SmsOptions
{
    public string Mode { get; set; } = "Pickup";
    public string PickupDirectory { get; set; } = "../tmp/travel-sms";
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string From { get; set; } = "";
    public bool Valid(IHostEnvironment env) => Mode == "Pickup" ?
        (env.IsDevelopment() || env.IsEnvironment("Testing")) && !string.IsNullOrWhiteSpace(PickupDirectory) :
        Mode == "Twilio" && Regex.IsMatch(AccountSid, "^AC[0-9a-fA-F]{32}$") && !string.IsNullOrEmpty(AuthToken) && Regex.IsMatch(From, @"^\+[1-9]\d{7,14}$");
}
public sealed class AmbiguousDeliveryException : Exception;
public record DeliveryResult(string Status, string? Reference = null);

public sealed class SmsTransport(HttpClient http, IOptions<SmsOptions> options, IHostEnvironment env)
{
    public async Task<DeliveryResult> Send(TravelNotification message, CancellationToken ct)
    {
        if (!Regex.IsMatch(message.Recipient, @"^\+[1-9]\d{7,14}$")) throw new InvalidOperationException("Telefon uluslararası biçimde olmalıdır.");
        if (options.Value.Mode == "Pickup")
        {
            if (!env.IsDevelopment() && !env.IsEnvironment("Testing")) throw new InvalidOperationException();
            var directory = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.PickupDirectory)); Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, message.Id + ".txt"), "To: " + message.Recipient + "\n" + message.Body, ct);
            return new("Preview");
        }
        try
        {
            using var request = Request(HttpMethod.Post, "Messages.json");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["To"] = message.Recipient, ["From"] = options.Value.From, ["Body"] = message.Body });
            using var response = await http.SendAsync(request, ct);
            if ((int)response.StatusCode >= 500) throw new AmbiguousDeliveryException();
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var sid = json.RootElement.GetProperty("sid").GetString();
            if (sid == null || !Regex.IsMatch(sid, "^SM[0-9a-fA-F]{32}$")) throw new AmbiguousDeliveryException();
            return new("Accepted", sid);
        }
        catch (HttpRequestException e) when (e.StatusCode == null) { throw new AmbiguousDeliveryException(); }
        catch (OperationCanceledException) { throw new AmbiguousDeliveryException(); }
        catch (JsonException) { throw new AmbiguousDeliveryException(); }
    }
    public async Task<DeliveryResult> Poll(string reference, CancellationToken ct)
    {
        if (!Regex.IsMatch(reference, "^SM[0-9a-fA-F]{32}$")) throw new InvalidOperationException();
        using var request = Request(HttpMethod.Get, "Messages/" + reference + ".json");
        using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (json.RootElement.GetProperty("sid").GetString() != reference) throw new InvalidOperationException("SMS referansı eşleşmedi.");
        var status = json.RootElement.GetProperty("status").GetString();
        return new(status switch { "delivered" or "read" => "Delivered", "failed" or "undelivered" or "canceled" => "Failed", _ => "Accepted" }, reference);
    }
    private HttpRequestMessage Request(HttpMethod method, string resource)
    {
        var request = new HttpRequestMessage(method, "https://api.twilio.com/2010-04-01/Accounts/" + options.Value.AccountSid + "/" + resource);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(options.Value.AccountSid + ":" + options.Value.AuthToken)));
        return request;
    }
}

public sealed class NotificationDelivery(TravelaDbContext db, EmailTransport email, SmsTransport sms, TimeProvider clock)
{
    public async Task Process(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow().UtcDateTime;
        await db.TravelNotifications.Where(n => n.Status == "Sending" && n.LeaseUntil <= now).ExecuteUpdateAsync(s => s
            .SetProperty(n => n.Status, "Unknown").SetProperty(n => n.LastError, "Gönderim sırasında işlem kesildi; yeniden göndermeden önce sağlayıcıyı kontrol edin.")
            .SetProperty(n => n.LeaseUntil, (DateTime?)null).SetProperty(n => n.LeaseToken, (Guid?)null), ct);
        var ids = await db.TravelNotifications.AsNoTracking().Where(n => (n.Status == "Pending" && n.Attempts < 8 || n.Status == "Accepted" && n.Channel == "Sms" && n.Polls < 2880) &&
            n.NextAttemptAt <= now && (n.LeaseUntil == null || n.LeaseUntil <= now)).OrderBy(n => n.NextAttemptAt).ThenBy(n => n.Id).Select(n => n.Id).Take(30).ToListAsync(ct);
        foreach (var id in ids)
        {
            var lease = Guid.NewGuid();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var message = await db.TravelNotifications.FromSqlInterpolated($"SELECT * FROM TravelNotifications WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}").SingleAsync(ct);
            if ((message.Status != "Pending" && !(message.Status == "Accepted" && message.Channel == "Sms")) || message.NextAttemptAt > now || message.LeaseUntil > now) { await tx.RollbackAsync(ct); continue; }
            var polling = message.Status == "Accepted";
            message.LeaseUntil = now.AddMinutes(2); message.LeaseToken = lease;
            if (!polling) { message.Status = "Sending"; message.Attempts++; } else message.Polls++;
            var attempt = new NotificationAttempt { NotificationId = id, CreatedAt = now, Status = polling ? "Polling" : "Started" };
            db.NotificationAttempts.Add(attempt); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); await tx.DisposeAsync();
            var next = "Pending"; string? error = null; var reference = message.ProviderReference;
            try
            {
                if (!polling && !await StillWanted(message, ct)) next = "Cancelled";
                else if (message.Channel == "Email")
                    next = await email.Send(id, message.Recipient, message.Subject, "<!doctype html><html lang=\"tr\"><meta charset=\"utf-8\"><body><h1>" + WebUtility.HtmlEncode(message.Subject) + "</h1><p>" + WebUtility.HtmlEncode(message.Body).Replace("\n", "<br>") + "</p></body></html>", ct);
                else
                {
                    var result = polling ? await sms.Poll(reference!, ct) : await sms.Send(message, ct);
                    next = result.Status; reference = result.Reference;
                    if (next == "Failed") error = "SMS sağlayıcısı teslim edilemedi sonucunu bildirdi.";
                }
            }
            catch (AmbiguousDeliveryException) { next = "Unknown"; error = "SMS gönderim sonucu belirsiz; otomatik tekrar çift gönderime yol açabileceğinden durduruldu."; }
            catch (Exception e) when (e is not OperationCanceledException)
            { next = polling ? "Accepted" : message.Attempts >= 8 ? "Failed" : "Pending"; error = "Gönderim/teslimat sorgusu başarısız; yapılandırmayı kontrol edin."; }
            if (polling && next == "Accepted" && message.Polls >= 2880) { next = "Unknown"; error = "48 saat içinde teslimat doğrulanamadı."; }
            message.Status = next; message.ProviderReference = reference; message.LastError = error; message.LeaseToken = null; message.LeaseUntil = null;
            message.NextAttemptAt = clock.GetUtcNow().UtcDateTime.AddSeconds(next == "Accepted" ? 60 : Math.Min(3600, 15 * Math.Pow(2, message.Attempts)));
            message.CompletedAt = next is "Pending" or "Sending" or "Accepted" ? null : clock.GetUtcNow().UtcDateTime;
            attempt.Status = next; attempt.Error = error; await db.SaveChangesAsync(ct);
        }
    }
    private async Task<bool> StillWanted(TravelNotification n, CancellationToken ct)
    {
        var r = await db.Reservations.AsNoTracking().Include(r => r.Trip).Include(r => r.Passenger).SingleAsync(r => r.Id == n.ReservationId, ct);
        var u = r.Passenger;
        if (!(n.Channel == "Email" ? u.EmailNotificationsEnabled : u.SmsNotificationsEnabled)) return false;
        return n.Type switch { "Reminder" => u.TripRemindersEnabled && r.Status == "Active" && r.Trip.Status == "Scheduled" && r.Trip.DepartureTime > clock.GetUtcNow().UtcDateTime && n.EventKey == "reminder:" + r.Trip.ChangeVersion,
            "Cancellation" => u.CancellationNotificationsEnabled, "RefundSucceeded" or "RefundFailed" => u.RefundNotificationsEnabled, _ => true };
    }
}

public sealed class TravelNotificationWorker(IServiceScopeFactory scopes, ILogger<TravelNotificationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TravelaDbContext>();
                await TravelNotifications.Reminders(db, DateTime.UtcNow, stoppingToken); db.ChangeTracker.Clear();
                await scope.ServiceProvider.GetRequiredService<NotificationDelivery>().Process(stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning("Yolculuk bildirim kuyruğu işlenemedi ({ErrorType}).", e.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
