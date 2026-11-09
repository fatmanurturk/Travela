using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Travela.Data;

namespace Travela.Api.Payments;

public sealed class PaymentConfigurationException : InvalidOperationException
{
    internal PaymentConfigurationException(string message) : base(message) { }
}

public static class PaymentStartupConfiguration
{
    public const string LiveOptionsName = "Live";
    public const string LiveSection = "Iyzico:Live";
    public const string SandboxSection = "Iyzico";

    public static bool AllowsTestProviders(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    public static string SelectProvider(IConfiguration configuration, IHostEnvironment environment)
    {
        var provider = configuration["Payments:Provider"];
        if (AllowsTestProviders(environment))
        {
            provider ??= "LocalTest";
            if (provider is "LocalTest" or "IyzicoSandbox")
                return provider;

            throw new PaymentConfigurationException(
                "Development/Testing ortamında yalnızca LocalTest veya IyzicoSandbox seçilebilir.");
        }

        // No default or fallback outside Development/Testing, including Production.
        if (provider != "Iyzico")
            throw new PaymentConfigurationException(
                "Üretimde Payments:Provider açıkça Iyzico olmalıdır; test sağlayıcıları kullanılamaz.");

        return provider;
    }

    public static void AddPaymentProviders(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var selected = SelectProvider(configuration, environment);
        var sandbox = selected == "IyzicoSandbox";
        if (selected != "LocalTest")
        {
            var section = sandbox ? SandboxSection : LiveSection;
            var options = configuration.GetSection(section).Get<IyzicoOptions>() ?? new IyzicoOptions();
            if (!options.ValidFor(sandbox))
                throw new PaymentConfigurationException(sandbox ? SandboxError : LiveError);
        }

        services.AddSingleton<LocalWebhookKey>();
        services.AddScoped<LocalTestPaymentProvider>();
        services.AddOptions<IyzicoOptions>().Bind(configuration.GetSection(SandboxSection))
            .Validate(options => selected != "IyzicoSandbox" || options.ValidFor(true), SandboxError)
            .ValidateOnStart();
        services.AddOptions<IyzicoOptions>(LiveOptionsName).Bind(configuration.GetSection(LiveSection))
            .Validate(options => selected != "Iyzico" || options.ValidFor(false), LiveError)
            .ValidateOnStart();

        services.AddHttpClient<IyzicoSandboxProvider>(http => http.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient("Travela.Iyzico.Live", http => http.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddTypedClient<IyzicoProvider>((http, sp) => new IyzicoProvider(
                http,
                sp.GetRequiredService<TravelaDbContext>(),
                Options.Create(sp.GetRequiredService<IOptionsMonitor<IyzicoOptions>>().Get(LiveOptionsName)),
                sp.GetRequiredService<IDataProtectionProvider>()));
        services.AddScoped<PaymentProviderRegistry>();
        services.AddScoped<IPaymentProvider>(sp =>
            sp.GetRequiredService<PaymentProviderRegistry>().Get(selected));
    }

    // Never include configuration values in errors: even an unknown provider or
    // malformed URL could contain a secret accidentally pasted into that field.
    private const string SandboxError =
        "Iyzico sandbox yapılandırması geçersiz: Iyzico altında sandbox API/secret anahtarları, " +
        "HTTPS callback adresi ve varsa sandbox ile uyumlu ApiBaseUrl gereklidir.";

    private const string LiveError =
        "Canlı Iyzico yapılandırması geçersiz: Iyzico:Live altında canlı API/secret anahtarları, " +
        "HTTPS CallbackBaseUrl, HTTPS FrontendBaseUrl ve varsa canlı ile uyumlu ApiBaseUrl gereklidir.";
}
