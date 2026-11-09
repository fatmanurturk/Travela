using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Travela.Data;

namespace Travela.Api.Payments;

// Registration and production configuration are handled separately at startup.
public sealed class IyzicoProvider(
    HttpClient http,
    TravelaDbContext db,
    IOptions<IyzicoOptions> options,
    IDataProtectionProvider protection) : IyzicoProviderBase(http, db, options, protection)
{
    public override string Name => "Iyzico";
    public override bool IsTest => false;
    public const string ApiBase = "https://api.iyzipay.com";
    protected override string ApiBaseUrl => ApiBase;
    protected override string CheckoutPageHost => "cpp.iyzipay.com";

    public static bool AllowedCheckoutUrl(string? value) =>
        IsCheckoutUrl(value, "api.iyzipay.com", "cpp.iyzipay.com");
}
