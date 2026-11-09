using Travela.Models;

namespace Travela.Api.Models;

public class LegalAcceptance
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string DocumentType { get; set; } = "";
    public string DocumentVersion { get; set; } = "";
    public string DocumentHash { get; set; } = "";
    public string DocumentSnapshot { get; set; } = "";
    public DateTime AcceptedAt { get; set; }
    public string IpAddress { get; set; } = "";
    public string Context { get; set; } = "";
    public Guid? RequestKey { get; set; }
    public Guid? PurchaseGroupId { get; set; }
    public Guid? PaymentOrderId { get; set; }
    public string? TransactionSnapshot { get; set; }
}
