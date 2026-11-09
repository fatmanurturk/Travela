using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

static class StartupHostChecks
{
    public static async Task Run()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var api = Path.Combine(root, "backend", "bin", configuration, "net10.0", "Travela.API.dll");
        var directory = Path.Combine(root, "tmp", "payment-startup-qa", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var appData = Path.Combine(directory, "appdata");
        Directory.CreateDirectory(appData);

        // Child processes have no usable SQL connection. A loopback listener without
        // a SQL server absorbs background account-worker connection attempts.
        using var databaseBlocker = new TcpListener(IPAddress.Loopback, 0);
        using var httpBlocker = new TcpListener(IPAddress.Loopback, 0);
        databaseBlocker.Start();
        httpBlocker.Start();
        var databasePort = ((IPEndPoint)databaseBlocker.LocalEndpoint).Port;
        var proxyPort = ((IPEndPoint)httpBlocker.LocalEndpoint).Port;
        var passed = 0;
        foreach (var test in StartupMatrix.Cases().Where(test => test.CheckHost))
        {
            using var portReservation = new TcpListener(IPAddress.Loopback, 0);
            portReservation.Start();
            var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
            portReservation.Stop();
            var values = test.Settings();
            var jwt = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(api);
            start.ArgumentList.Add("--contentRoot");
            start.ArgumentList.Add(directory);
            foreach (var key in start.Environment.Keys.ToArray())
            {
                if (new[] { "Payments", "Iyzico", "Jwt", "AccountEmail", "TravelSms", "ConnectionStrings" }
                    .Any(prefix => key.StartsWith(prefix + "__", StringComparison.OrdinalIgnoreCase) ||
                        key.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase)))
                    start.Environment.Remove(key);
            }
            start.Environment.Remove("DOTNET_ENVIRONMENT");
            start.Environment["ASPNETCORE_ENVIRONMENT"] = test.Environment;
            start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
            start.Environment["APPDATA"] = appData;
            start.Environment["LOCALAPPDATA"] = appData;
            start.Environment["Jwt__Key"] = jwt;
            start.Environment["Jwt__Issuer"] = "StartupTests";
            start.Environment["Jwt__Audience"] = "StartupTests";
            start.Environment["ConnectionStrings__DefaultConnection"] =
                $"Server=tcp:127.0.0.1,{databasePort};Database=StartupTestsUnavailable;" +
                "Integrated Security=true;Encrypt=true;TrustServerCertificate=true;" +
                "Connect Timeout=1;ConnectRetryCount=0;Pooling=false";
            start.Environment["Payments__RecoveryEnabled"] = "false";
            start.Environment["Notifications__WorkerEnabled"] = "false";
            start.Environment["Trips__LifecycleEnabled"] = "false";
            start.Environment["AccountEmail__DeliveryMode"] = "Smtp";
            start.Environment["AccountEmail__SmtpHost"] = "smtp.example.test";
            start.Environment["AccountEmail__FrontendBaseUrl"] = "https://frontend.example.test";
            start.Environment["AccountEmail__FromAddress"] = "noreply@example.test";
            start.Environment["TravelSms__Mode"] = "Twilio";
            start.Environment["TravelSms__AccountSid"] = "AC" + Guid.NewGuid().ToString("N");
            start.Environment["TravelSms__AuthToken"] = Guid.NewGuid().ToString("N");
            start.Environment["TravelSms__From"] = "+10000000000";
            start.Environment["Logging__LogLevel__Default"] = "Information";
            start.Environment["Logging__LogLevel__Microsoft.EntityFrameworkCore"] = "Critical";
            start.Environment["Logging__EventLog__LogLevel__Default"] = "None";
            // Even an accidental outbound HTTP request cannot reach a provider.
            start.Environment["HTTP_PROXY"] = $"http://127.0.0.1:{proxyPort}";
            start.Environment["HTTPS_PROXY"] = $"http://127.0.0.1:{proxyPort}";
            start.Environment["ALL_PROXY"] = $"http://127.0.0.1:{proxyPort}";
            start.Environment["NO_PROXY"] = "localhost,127.0.0.1";
            foreach (var pair in values.Where(pair => pair.Value != null))
                start.Environment[pair.Key.Replace(":", "__")] = pair.Value;

            using var process = Process.Start(start) ?? throw new Exception("Test API süreci başlatılamadı.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            var ready = false;
            try
            {
                using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
                {
                    BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                    Timeout = TimeSpan.FromSeconds(1)
                };
                var watch = Stopwatch.StartNew();
                while (!process.HasExited && watch.Elapsed < TimeSpan.FromSeconds(15))
                {
                    try
                    {
                        using var response = await http.GetAsync("/api/purchases/options");
                        if (response.IsSuccessStatusCode)
                        {
                            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                            var provider = json.RootElement.GetProperty("provider").GetString();
                            ready = provider == (test.Provider ?? "LocalTest") &&
                                json.RootElement.GetProperty("isSandbox").GetBoolean() ==
                                (test.Environment is "Development" or "Testing");
                            break;
                        }
                    }
                    catch (HttpRequestException) { }
                    catch (TaskCanceledException) { }
                    await Task.Delay(50);
                }
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            var log = await output + await errors;
            if (StartupMatrix.ContainsSecret(log, values) || log.Contains(jwt, StringComparison.Ordinal))
                throw new Exception("Başlangıç çıktısında gizli test değeri bulundu; çıktı gösterilmedi.");
            if (ready != test.Starts || (!test.Starts &&
                (process.ExitCode != 1 ||
                 !log.Contains("[PAYMENTS_STARTUP_REJECTED]", StringComparison.Ordinal))))
                throw new Exception("API başlangıç matrisi başarısız: " + test.Name + Environment.NewLine + log);
            if (httpBlocker.Pending())
                throw new Exception("Başlangıç sırasında beklenmeyen dış HTTP girişimi engellendi.");

            passed++;
            Console.WriteLine("PASS: " + test.Name);
        }
        Console.WriteLine($"PASS: {passed} gerçek API süreci başlangıç senaryosu; " +
            "ASPNETCORE_ENVIRONMENT ile doğrulandı, ödeme/SMS/SMTP gönderimi ve SQL veritabanı kullanılmadı.");
    }
}
