using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Travela.Api.Models;
using Travela.Data;

namespace Travela.Api.Payments;

public sealed class LocalWebhookKey
{
    // Only a local test key. Never exposed to browser code or persisted in source/logs.
    public byte[] Bytes { get; } = RandomNumberGenerator.GetBytes(32);
}

public class LocalTestPaymentProvider(TravelaDbContext db, LocalWebhookKey key, TimeProvider clock) : IPaymentProvider
{
    public string Name => "LocalTest";
    public bool IsTest => true;
    public async Task<string> OpenCheckout(Guid orderId, decimal amount, string currency, CancellationToken ct)
    {
        // SQL insert-if-absent is idempotent for concurrent clicks and recovery after a lost response.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO LocalPaymentRecords (Id, Amount, Currency, Status, EventId, RefundOutcome, RefundTransfers) SELECT {orderId}, {amount}, {currency}, N'Pending', {Guid.NewGuid().ToString("N")}, N'Succeeded', 0 WHERE NOT EXISTS (SELECT 1 FROM LocalPaymentRecords WITH (UPDLOCK, HOLDLOCK) WHERE Id = {orderId})", ct);
        var group = await db.PurchaseGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == orderId, ct);
        if (group != null)
        {
            foreach (var line in await db.PaymentOrders.AsNoTracking().Where(p => p.PurchaseGroupId == orderId).ToListAsync(ct))
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO LocalPaymentRecords (Id, Amount, Currency, Status, EventId, RefundOutcome, RefundTransfers) SELECT {line.Id}, {line.Amount}, {currency}, N'Pending', {Guid.NewGuid().ToString("N")}, N'Succeeded', 0 WHERE NOT EXISTS (SELECT 1 FROM LocalPaymentRecords WITH (UPDLOCK, HOLDLOCK) WHERE Id = {line.Id})", ct);
            return $"/purchase/{orderId}";
        }
        return $"/payment/{orderId}";
    }
    public async Task<ProviderPayment> GetPayment(Guid orderId, CancellationToken ct)
    {
        var p = await db.LocalPaymentRecords.AsNoTracking().SingleAsync(p => p.Id == orderId, ct);
        var group = await db.PurchaseGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == orderId, ct);
        if (group != null)
        {
            var state = p.Status == "Pending" && group.HoldExpiresAt <= clock.GetUtcNow().UtcDateTime ? "Failed" : p.Status;
            var lines = await db.PaymentOrders.AsNoTracking().Where(o => o.PurchaseGroupId == orderId).Select(o => new ProviderLine(o.Id, "TEST-" + orderId.ToString("N") + "-" + o.Id.ToString("N"), o.Amount)).ToListAsync(ct);
            return new(orderId, "TEST-" + orderId.ToString("N"), state, p.Amount, p.Currency, true, lines);
        }
        var expiry = await db.PaymentOrders.Where(o => o.Id == orderId).Select(o => o.Reservation.HoldExpiresAt).SingleAsync(ct);
        var status = p.Status == "Pending" && expiry <= clock.GetUtcNow().UtcDateTime ? "Failed" : p.Status;
        return new(orderId, "TEST-" + orderId.ToString("N"), status, p.Amount, p.Currency, true);
    }
    public string Sign(ProviderNotice notice) => Convert.ToHexString(HMACSHA256.HashData(key.Bytes, Encoding.UTF8.GetBytes($"{notice.OrderId:N}|{notice.EventId}|{notice.Timestamp}")));
    public bool Verify(ProviderNotice notice, string signature)
    {
        if (Math.Abs((decimal)clock.GetUtcNow().ToUnixTimeSeconds() - notice.Timestamp) > 300) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Sign(notice)), Convert.FromHexString(signature)); }
        catch (FormatException) { return false; }
    }
    public async Task<ProviderNotice> Simulate(Guid id, string outcome, string refundOutcome, CancellationToken ct)
    {
        if (await db.PaymentOrders.AnyAsync(p => p.Id == id && p.PurchaseGroupId != null, ct)) throw new PaymentFlowException("Ortak sipariş tek ödeme olarak tamamlanmalıdır.");
        if (outcome is not ("Succeeded" or "Failed") || refundOutcome is not ("Succeeded" or "Pending" or "Failed"))
            throw new PaymentFlowException("Geçersiz işlem sonucu.");
        await db.LocalPaymentRecords.Where(p => p.Id == id && p.Status == "Pending")
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, outcome).SetProperty(p => p.RefundOutcome, refundOutcome), ct);
        var p = await db.LocalPaymentRecords.AsNoTracking().SingleAsync(p => p.Id == id, ct);
        if (await db.PurchaseGroups.AnyAsync(g => g.Id == id, ct))
        {
            var lines = db.PaymentOrders.Where(o => o.PurchaseGroupId == id).Select(o => o.Id);
            await db.LocalPaymentRecords.Where(r => lines.Contains(r.Id) && r.Status == "Pending").ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, p.Status).SetProperty(r => r.RefundOutcome, refundOutcome), ct);
        }
        return new(id, p.EventId, clock.GetUtcNow().ToUnixTimeSeconds());
    }
    public async Task<ProviderRefund> Refund(Guid orderId, decimal amount, string refundKey, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var p = await db.LocalPaymentRecords.FromSqlInterpolated($"SELECT * FROM LocalPaymentRecords WITH (UPDLOCK, HOLDLOCK) WHERE Id = {orderId}").SingleAsync(ct);
        if (p.Status != "Succeeded" || p.Amount != amount || (p.RefundKey != null && p.RefundKey != refundKey))
            return new("Failed", null, "Test sağlayıcı tutar veya işlem anahtarı uyuşmazlığı.");
        p.RefundKey = refundKey;
        if (p.RefundTransfers == 0 && p.RefundOutcome == "Succeeded") p.RefundTransfers = 1;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return p.RefundTransfers == 1 ? new("Succeeded", "TEST-REFUND-" + refundKey, null) :
            new(p.RefundOutcome, null, p.RefundOutcome == "Failed" ? "İade işlemi tamamlanamadı." : null);
    }
}
