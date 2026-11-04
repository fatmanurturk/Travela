using Microsoft.EntityFrameworkCore;
using Travela.Data;

namespace Travela.Api.Payments;

public class PaymentRecoveryWorker(IServiceScopeFactory scopes, ILogger<PaymentRecoveryWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TravelaDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<PaymentService>();
                await db.PaymentOrders.Where(p => p.RefundAttempts >= 8 && (p.RefundStatus == "Pending" || p.RefundStatus == "Failed") &&
                    (p.RefundLeaseUntil == null || p.RefundLeaseUntil <= DateTime.UtcNow))
                    .ExecuteUpdateAsync(update => update.SetProperty(p => p.NeedsAttention, true), stoppingToken);
                await service.ExpireHolds(stoppingToken);
                var now = DateTime.UtcNow;
                await db.PurchaseGroups.Where(g => g.HoldExpiresAt <= now && g.ProtectedBuyerJson != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.ProtectedBuyerJson, (string?)null), stoppingToken);
                var ids = await db.PaymentOrders.AsNoTracking().Where(p => p.PurchaseGroupId == null && p.PaymentStatus == "Pending" && (p.NextReconciliationAt == null || p.NextReconciliationAt <= now))
                    .OrderBy(p => p.NextReconciliationAt ?? p.CreatedAt).ThenBy(p => p.Id).Select(p => p.Id).Take(20).ToListAsync(stoppingToken);
                await db.PurchaseGroups.Where(g => g.CheckoutState == "Initializing" && g.CheckoutLeaseUntil <= now).ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.CheckoutState, "Unknown").SetProperty(g => g.NeedsAttention, true)
                    .SetProperty(g => g.LastError, "Ödeme sayfası başlatılırken işlem kesildi; sağlayıcı mutabakatı gerekiyor."), stoppingToken);
                var groups = await db.PurchaseGroups.AsNoTracking().Where(g => g.PaymentStatus == "Pending" && (g.NextReconciliationAt == null || g.NextReconciliationAt <= now))
                    .OrderBy(g => g.NextReconciliationAt ?? g.CreatedAt).ThenBy(g => g.Id).Select(g => g.Id).Take(20).ToListAsync(stoppingToken);
                var refunds = await db.PaymentOrders.AsNoTracking().Where(p => (p.RefundStatus == "Pending" || p.RefundStatus == "Failed") && p.RefundAttempts < 8 && p.NextRefundAttemptAt <= now).OrderBy(p => p.NextRefundAttemptAt).ThenBy(p => p.Id).Select(p => p.Id).Take(20).ToListAsync(stoppingToken);
                // Separate bounded queues: old single payments cannot delay new carts or refunds.
                await Task.WhenAll(ProcessBatch(ids, "payment", stoppingToken), ProcessBatch(groups, "group", stoppingToken), ProcessBatch(refunds, "refund", stoppingToken));
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested) { log.LogError(e, "Ödeme/iade takip döngüsü tamamlanamadı."); }
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }
    private Task ProcessBatch(IEnumerable<Guid> ids, string kind, CancellationToken ct) => Parallel.ForEachAsync(ids,
        new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, async (id, token) =>
        {
            try
            {
                using var scope = scopes.CreateScope(); var payments = scope.ServiceProvider.GetRequiredService<PaymentService>();
                if (kind == "group") await payments.ReconcileGroup(id, token);
                else if (kind == "refund") await payments.ProcessRefund(id, token);
                else await payments.Reconcile(id, token);
            }
            catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested) { log.LogWarning("Ödeme takip işlemi tamamlanamadı: {Kind}, {OrderId}, {ErrorType}", kind, id, e.GetType().Name); }
        });
}
