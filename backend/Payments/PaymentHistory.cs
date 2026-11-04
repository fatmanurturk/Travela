using Travela.Api.Models;
using Travela.Data;

namespace Travela.Api.Payments;

public static class PaymentHistory
{
    public static PaymentEvent Record(TravelaDbContext db, PaymentOrder order, string type, DateTime now,
        int? actorId = null, string? actorName = null, string? reason = null, Guid? requestKey = null)
    {
        var entry = new PaymentEvent { OrderId = order.Id, Type = type, CreatedAt = now, PaymentStatus = order.PaymentStatus,
            RefundStatus = order.RefundStatus, RefundAmount = order.RefundAmount, ActorUserId = actorId, ActorName = actorName,
            Reason = reason, RequestKey = requestKey };
        db.PaymentEvents.Add(entry);
        return entry;
    }
}
