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
                await service.ExpireHolds(stoppingToken);
                var ids = await db.PaymentOrders.AsNoTracking().Where(p => p.PaymentStatus == "Pending").OrderBy(p => p.CreatedAt).Select(p => p.Id).Take(100).ToListAsync(stoppingToken);
                foreach (var id in ids)
                {
                    try { await service.Reconcile(id, stoppingToken); }
                    catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Ödeme mutabakatı tamamlanamadı: {OrderId}", id); }
                }
                var refunds = await db.PaymentOrders.AsNoTracking().Where(p => (p.RefundStatus == "Pending" || p.RefundStatus == "Failed") && p.RefundAttempts < 8 && p.NextRefundAttemptAt <= DateTime.UtcNow).Select(p => p.Id).Take(100).ToListAsync(stoppingToken);
                foreach (var id in refunds)
                {
                    try { await service.ProcessRefund(id, stoppingToken); }
                    catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "İade takip işlemi tamamlanamadı: {OrderId}", id); }
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Ödeme/iade takip döngüsü tamamlanamadı."); }
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }
}
