using System.Text.Json;
using Travela.DTOs;

namespace Travela.Api.Payments;

public sealed class IyzicoOptions
{
    public string ApiKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string CallbackBaseUrl { get; set; } = "";
    public string FrontendBaseUrl { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "";

    public bool Valid =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(SecretKey) &&
        Uri.TryCreate(CallbackBaseUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);

    public bool ValidFor(bool sandbox)
    {
        if (!Valid || ApiKey.Any(char.IsWhiteSpace) || SecretKey.Any(char.IsWhiteSpace) ||
            IsSandboxKey(ApiKey) != sandbox || IsSandboxKey(SecretKey) != sandbox)
            return false;

        // Provider URLs are fixed in code. A supplied URL must agree with that
        // environment; it can never redirect credentials to another endpoint.
        var expected = sandbox ? IyzicoSandboxProvider.ApiBase : IyzicoProvider.ApiBase;
        if (!string.IsNullOrEmpty(ApiBaseUrl) &&
            (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var api) ||
             !string.IsNullOrEmpty(api.UserInfo) || !string.IsNullOrEmpty(api.Query) ||
             !string.IsNullOrEmpty(api.Fragment) || api != new Uri(expected)))
            return false;

        return sandbox ||
            (Uri.TryCreate(FrontendBaseUrl, UriKind.Absolute, out var frontend) &&
             frontend.Scheme == "https" && string.IsNullOrEmpty(frontend.UserInfo) &&
             string.IsNullOrEmpty(frontend.Query) && string.IsNullOrEmpty(frontend.Fragment));
    }

    private static bool IsSandboxKey(string key) =>
        key.StartsWith("sandbox-", StringComparison.OrdinalIgnoreCase);
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
