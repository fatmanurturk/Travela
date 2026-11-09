using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Travela.Data;

namespace Travela.Api.Payments;

public sealed class IyzicoSandboxProvider(
    HttpClient http,
    TravelaDbContext db,
    IOptions<IyzicoOptions> options,
    IDataProtectionProvider protection) : IyzicoProviderBase(http, db, options, protection)
{
    public override string Name => "IyzicoSandbox";
    public override bool IsTest => true;
    public const string ApiBase = "https://sandbox-api.iyzipay.com";
    protected override string ApiBaseUrl => ApiBase;
    protected override string CheckoutPageHost => "sandbox-cpp.iyzipay.com";

    public static bool AllowedCheckoutUrl(string? value) =>
        IsCheckoutUrl(value, "sandbox-api.iyzipay.com", "sandbox-cpp.iyzipay.com");
}
