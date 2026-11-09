using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Travela.Data;
using Travela.Api.Notifications;

namespace Travela.Api.Security;

public sealed class AccountEmailWorker(IServiceScopeFactory scopes, ILogger<AccountEmailWorker> log, IOptions<QueueWorkerOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = settings.ErrorIntervalSeconds;
                try
                {
                    using var scope = scopes.CreateScope();
                    var count = await scope.ServiceProvider.GetRequiredService<AccountEmailDelivery>().Process(stoppingToken);
                    delay = count == 0 ? settings.IdleIntervalSeconds : settings.PollIntervalSeconds;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception e)
                { log.LogWarning("Hesap e-posta kuyruğu işlenemedi ({ErrorType}).", e.GetType().Name); }
                await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

public sealed class AccountEmailDelivery(TravelaDbContext db, AccountEmailService links, IOptions<AccountEmailOptions> options,
    IHostEnvironment env, TimeProvider clock, ILogger<AccountEmailDelivery> log)
{
    public async Task<int> Process(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.AccountEmailMessages.Where(m => m.Status == "Pending" && m.Attempts >= 8 && (m.LeaseUntil == null || m.LeaseUntil <= now))
            .ExecuteUpdateAsync(update => update.SetProperty(m => m.Status, "Failed").SetProperty(m => m.LeaseToken, (Guid?)null)
                .SetProperty(m => m.LeaseUntil, (DateTime?)null).SetProperty(m => m.LastError, "E-posta teslim denemeleri tükendi; gönderim yapılandırmasını kontrol edin."), ct);
        var ids = await db.AccountEmailMessages.AsNoTracking().Where(m => m.Status == "Pending" && m.Attempts < 8 &&
                m.NextAttemptAt <= now && (m.LeaseUntil == null || m.LeaseUntil <= now))
            .OrderBy(m => m.NextAttemptAt).ThenBy(m => m.Id).Select(m => m.Id).Take(10).ToListAsync(ct);
        foreach (var id in ids)
        {
            var lease = Guid.NewGuid();
            if (await db.AccountEmailMessages.Where(m => m.Id == id && m.Status == "Pending" && m.Attempts < 8 &&
                    m.NextAttemptAt <= now && (m.LeaseUntil == null || m.LeaseUntil <= now))
                .ExecuteUpdateAsync(update => update.SetProperty(m => m.LeaseToken, lease).SetProperty(m => m.LeaseUntil, now.AddMinutes(2))
                    .SetProperty(m => m.Attempts, m => m.Attempts + 1), ct) != 1) continue;
            var message = await db.AccountEmailMessages.AsNoTracking().SingleAsync(m => m.Id == id, ct);
            var status = "Cancelled";
            string? error = null;
            try
            {
                if (await db.Users.EnabledAccounts().WithActiveCompany(db).AnyAsync(u => u.Id == message.UserId && u.Email == message.Recipient &&
                        u.PasswordResetTokenHash == message.TokenHash && u.PasswordResetTokenUsedAt == null && u.PasswordResetTokenExpiresAt > clock.GetUtcNow().UtcDateTime, ct))
                {
                    var html = links.Protector.Unprotect(message.ProtectedBody);
                    await new EmailTransport(options, env).Send(message.Id, message.Recipient, message.Subject, html, ct);
                    status = "Sent";
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                status = message.Attempts >= 8 ? "Failed" : "Pending";
                error = "E-posta teslim edilemedi; gönderim yapılandırmasını kontrol edin.";
                log.LogWarning("Hesap e-postası teslim edilemedi: {MessageId}, {ErrorType}", id, e.GetType().Name);
            }
            var completed = status is "Sent" or "Cancelled";
            await db.AccountEmailMessages.Where(m => m.Id == id && m.LeaseToken == lease)
                .ExecuteUpdateAsync(update => update.SetProperty(m => m.Status, status).SetProperty(m => m.LastError, error)
                    .SetProperty(m => m.LeaseToken, (Guid?)null).SetProperty(m => m.LeaseUntil, (DateTime?)null)
                    .SetProperty(m => m.CompletedAt, completed ? clock.GetUtcNow().UtcDateTime : (DateTime?)null)
                    .SetProperty(m => m.ProtectedBody, completed ? "" : message.ProtectedBody)
                    .SetProperty(m => m.NextAttemptAt, clock.GetUtcNow().UtcDateTime.AddSeconds(Math.Min(3600, 15 * Math.Pow(2, message.Attempts)))), ct);
        }
        return ids.Count;
    }
}
