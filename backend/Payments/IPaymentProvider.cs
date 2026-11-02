namespace Travela.Api.Payments;

public record ProviderPayment(Guid OrderId, string Reference, string Status, decimal Amount, string Currency, bool IsTest);
public record ProviderRefund(string Status, string? Reference, string? Error);
public record ProviderNotice(Guid OrderId, string EventId, long Timestamp);

public interface IPaymentProvider
{
    string Name { get; }
    bool IsTest { get; }
    Task<string> OpenCheckout(Guid orderId, decimal amount, string currency, CancellationToken ct);
    Task<ProviderPayment> GetPayment(Guid orderId, CancellationToken ct);
    // Implementations must honour the same key across timeouts, retries and process restarts.
    Task<ProviderRefund> Refund(Guid orderId, decimal amount, string key, CancellationToken ct);
    bool Verify(ProviderNotice notice, string signature);
}
