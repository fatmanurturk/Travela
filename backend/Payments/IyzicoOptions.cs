using System.Text.Json;
using Travela.DTOs;

namespace Travela.Api.Payments;

public sealed class IyzicoOptions
{
    public string ApiKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string CallbackBaseUrl { get; set; } = "";

    public bool Valid =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(SecretKey) &&
        Uri.TryCreate(CallbackBaseUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);
}

public record IyzicoBuyer(CheckoutBuyerDto Buyer, string Email, string Ip);

public sealed class IyzicoNotice
{
    public string IyziEventType { get; set; } = "";
    public JsonElement IyziPaymentId { get; set; }
    public string Token { get; set; } = "";
    public string PaymentConversationId { get; set; } = "";
    public string Status { get; set; } = "";
    public string IyziReferenceCode { get; set; } = "";
}
