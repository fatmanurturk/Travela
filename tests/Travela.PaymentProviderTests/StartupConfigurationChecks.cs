using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Travela.Api.Payments;
using Travela.Data;

static class StartupConfigurationChecks
{
    public static void Run()
    {
        var passed = 0;
        foreach (var test in StartupMatrix.Cases())
        {
            var values = test.Settings();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var environment = new TestEnvironment(test.Environment);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHostEnvironment>(environment);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.AddScoped(_ => new TravelaDbContext(new DbContextOptionsBuilder<TravelaDbContext>().Options));
            try
            {
                services.AddPaymentProviders(configuration, environment);
                using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
                container.GetRequiredService<IStartupValidator>().Validate();
                using var scope = container.CreateScope();
                var active = scope.ServiceProvider.GetRequiredService<IPaymentProvider>();
                Check(test.Starts && active.Name == (test.Provider ?? "LocalTest"), test.Name);
                passed++;

                var registry = scope.ServiceProvider.GetRequiredService<PaymentProviderRegistry>();
                var testEnvironment = PaymentStartupConfiguration.AllowsTestProviders(environment);
                foreach (var forbidden in testEnvironment
                    ? new[] { "Iyzico", "unknown" }
                    : new[] { "LocalTest", "IyzicoSandbox", "unknown" })
                {
                    try
                    {
                        registry.Get(forbidden);
                        throw new Exception("Ortam dışı sağlayıcı çözümlendi.");
                    }
                    catch (PaymentFlowException error) when (error.Status == 503)
                    {
                        Check(!StartupMatrix.ContainsSecret(error.ToString(), values), test.Name);
                        passed++;
                    }
                }
            }
            catch (InvalidOperationException error)
            {
                Check(!test.Starts && !StartupMatrix.ContainsSecret(error.ToString(), values), test.Name);
                passed++;
            }
        }
        Console.WriteLine($"PASS: {passed} başlangıç/DI/anahtar gizleme kontrolü; ağ ve veritabanı kullanılmadı.");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new Exception("Başlangıç kontrolü başarısız: " + name);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "PaymentStartupTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

sealed record StartupCase(
    string Name,
    string Environment,
    string? Provider,
    bool Starts,
    Action<Dictionary<string, string?>>? Change = null,
    bool CheckHost = false)
{
    public Dictionary<string, string?> Settings()
    {
        var values = new Dictionary<string, string?>
        {
            ["Payments:Provider"] = Provider,
            ["Iyzico:ApiKey"] = "sandbox-" + StartupMatrix.ApiKey,
            ["Iyzico:SecretKey"] = "sandbox-" + StartupMatrix.SecretKey,
            ["Iyzico:CallbackBaseUrl"] = "https://callback.example.test",
            ["Iyzico:Live:ApiKey"] = StartupMatrix.ApiKey,
            ["Iyzico:Live:SecretKey"] = StartupMatrix.SecretKey,
            ["Iyzico:Live:CallbackBaseUrl"] = "https://callback.example.test",
            ["Iyzico:Live:FrontendBaseUrl"] = "https://frontend.example.test"
        };
        Change?.Invoke(values);
        return values;
    }
}

static class StartupMatrix
{
    // Ephemeral test credentials, never read from the user's environment or written to a config file.
    public static string ApiKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string SecretKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static bool ContainsSecret(string output, Dictionary<string, string?> values) =>
        values.Where(pair => pair.Key.EndsWith("ApiKey") || pair.Key.EndsWith("SecretKey"))
            .Select(pair => pair.Value).OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Any(value => output.Contains(value, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<StartupCase> Cases()
    {
        foreach (var (provider, label) in new (string? Provider, string Label)[]
        {
            (null, "eksik"),
            ("", "boş"),
            (" ", "yalnızca boşluk"),
            ("LocalTest", "LocalTest"),
            ("IyzicoSandbox", "IyzicoSandbox"),
            ("unknown", "tanımsız"),
            (ApiKey, "sağlayıcı alanına yanlışlıkla yapıştırılmış anahtar")
        })
            yield return new("Production sağlayıcı reddi: " + label, "Production", provider, false, CheckHost: true);

        yield return new("Production eksiksiz canlı ayarlar", "Production", "Iyzico", true, CheckHost: true);
        yield return new("Production açık canlı API adresi", "Production", "Iyzico", true,
            v => v["Iyzico:Live:ApiBaseUrl"] = IyzicoProvider.ApiBase, true);

        foreach (var field in new[] { "ApiKey", "SecretKey", "CallbackBaseUrl", "FrontendBaseUrl" })
            yield return new("Production eksik alan: " + field, "Production", "Iyzico", false,
                v => v.Remove("Iyzico:Live:" + field), true);

        foreach (var field in new[] { "ApiKey", "SecretKey" })
        {
            yield return new("Production sandbox anahtarı: " + field, "Production", "Iyzico", false,
                v => v["Iyzico:Live:" + field] = "sandbox-" + v["Iyzico:Live:" + field], true);
            yield return new("Production boş anahtar: " + field, "Production", "Iyzico", false,
                v => v["Iyzico:Live:" + field] = " ");
            yield return new("Production boşluklu anahtar: " + field, "Production", "Iyzico", false,
                v => v["Iyzico:Live:" + field] += " ");
        }

        foreach (var field in new[] { "CallbackBaseUrl", "FrontendBaseUrl" })
        {
            foreach (var url in new[]
            {
                "http://example.test",
                "/relative",
                "https://" + ApiKey + ":" + SecretKey + "@example.test",
                "https://example.test/?value=" + ApiKey,
                "https://example.test/#" + SecretKey
            })
                yield return new("Production güvensiz adres: " + field, "Production", "Iyzico", false,
                    v => v["Iyzico:Live:" + field] = url, url.StartsWith("http://"));
        }

        foreach (var api in new[]
        {
            IyzicoSandboxProvider.ApiBase,
            "http://api.iyzipay.com",
            "https://api.iyzipay.com.evil.test",
            "https://api.iyzipay.com/?value=" + ApiKey,
            "https://api.iyzipay.com/#" + SecretKey
        })
            yield return new("Production yanlış API adresi", "Production", "Iyzico", false,
                v => v["Iyzico:Live:ApiBaseUrl"] = api, api == IyzicoSandboxProvider.ApiBase);

        yield return new("Production sandbox bölümünden canlı anahtar devralmaz", "Production", "Iyzico", false,
            v =>
            {
                v.Remove("Iyzico:Live:ApiKey");
                v.Remove("Iyzico:Live:SecretKey");
                v["Iyzico:ApiKey"] = ApiKey;
                v["Iyzico:SecretKey"] = SecretKey;
            }, true);

        foreach (var environment in new[] { "Development", "Testing" })
        {
            yield return new(environment + " varsayılan LocalTest", environment, null, true, CheckHost: true);
            yield return new(environment + " açık LocalTest", environment, "LocalTest", true);
            yield return new(environment + " mevcut sandbox ayarları", environment, "IyzicoSandbox", true,
                CheckHost: true);
            yield return new(environment + " açık sandbox URL", environment, "IyzicoSandbox", true,
                v => v["Iyzico:ApiBaseUrl"] = IyzicoSandboxProvider.ApiBase);
            yield return new(environment + " canlı sağlayıcı reddedilir", environment, "Iyzico", false);
            yield return new(environment + " sandbox/canlı URL karışımı", environment, "IyzicoSandbox", false,
                v => v["Iyzico:ApiBaseUrl"] = IyzicoProvider.ApiBase, true);
            yield return new(environment + " sandbox/canlı API anahtarı karışımı", environment, "IyzicoSandbox", false,
                v => v["Iyzico:ApiKey"] = ApiKey, true);
            yield return new(environment + " sandbox/canlı secret anahtarı karışımı", environment, "IyzicoSandbox", false,
                v => v["Iyzico:SecretKey"] = SecretKey);
        }

        yield return new("Staging varsayılana düşmez", "Staging", null, false);
        yield return new("Staging test sağlayıcısı kullanamaz", "Staging", "LocalTest", false);
        yield return new("Staging canlı ayarları zorunludur", "Staging", "Iyzico", true);
    }
}
