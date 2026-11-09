using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Travela.Api.Notifications;
using Travela.Api.Security;

// No web host, database connection, or transport is started by these checks.
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
foreach (var development in new[] { false, true })
{
    var config = new ConfigurationBuilder().SetBasePath(Path.Combine(root, "backend")).AddJsonFile("appsettings.json");
    if (development) config.AddJsonFile("appsettings.Development.json");
    var configuration = config.Build();
    using var loggerFactory = LoggerFactory.Create(logging => logging.AddConfiguration(configuration.GetSection("Logging")).AddProvider(new ProbeProvider()));
    var sql = loggerFactory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");
    Check(!sql.IsEnabled(LogLevel.Information) && sql.IsEnabled(LogLevel.Warning) && sql.IsEnabled(LogLevel.Error), "SQL log levels");
    Check(loggerFactory.CreateLogger("Travela.Worker").IsEnabled(LogLevel.Information), "Application information logs remain enabled");
    var account = configuration.GetSection("AccountEmail:Worker").Get<QueueWorkerOptions>()!;
    var travel = configuration.GetSection("Notifications:Worker").Get<TravelNotificationWorkerOptions>()!;
    Check(account.Valid && account.PollIntervalSeconds == 2 && account.IdleIntervalSeconds == 10 && account.ErrorIntervalSeconds == 30, "Account intervals bind");
    Check(travel.Valid && travel.PollIntervalSeconds == 15 && travel.IdleIntervalSeconds == 30 && travel.ReminderIntervalSeconds == 60, "Travel intervals bind");
}
Check(!new QueueWorkerOptions { PollIntervalSeconds = 0 }.Valid, "Zero interval rejected");
Check(!new QueueWorkerOptions { IdleIntervalSeconds = -1 }.Valid, "Negative interval rejected");
Check(!new QueueWorkerOptions { ErrorIntervalSeconds = 3601 }.Valid, "Excessive interval rejected");

await CheckWorker(scopes => new AccountEmailWorker(scopes, NullLogger<AccountEmailWorker>.Instance,
    Options.Create(new QueueWorkerOptions { PollIntervalSeconds = 1, IdleIntervalSeconds = 1, ErrorIntervalSeconds = 1 })));
await CheckWorker(scopes => new TravelNotificationWorker(scopes, NullLogger<TravelNotificationWorker>.Instance,
    Options.Create(new TravelNotificationWorkerOptions { PollIntervalSeconds = 1, IdleIntervalSeconds = 1, ErrorIntervalSeconds = 1 }), TimeProvider.System));
Console.WriteLine("PASS: configuration, SQL logging filters, worker error pacing and cancellation (no database or delivery).");

static async Task CheckWorker(Func<FailingScopes, BackgroundService> create)
{
    var scopes = new FailingScopes();
    using var worker = create(scopes);
    await worker.StartAsync(default);
    var deadline = Stopwatch.StartNew();
    while (scopes.Count == 0 && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
    Check(scopes.Count == 1, "Worker starts immediately");
    await Task.Delay(150);
    Check(scopes.Count == 1, "Failure does not create a tight retry loop");
    while (scopes.Count < 2 && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
    Check(scopes.Count == 2 && deadline.Elapsed >= TimeSpan.FromMilliseconds(900), "Worker retries after configured error delay");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    await worker.StopAsync(timeout.Token);
    Check(worker.ExecuteTask!.IsCompletedSuccessfully, "Cancellation completes gracefully during delay");
    var count = scopes.Count;
    await Task.Delay(100);
    Check(scopes.Count == count, "No work after stop");
}
static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
}
sealed class FailingScopes : IServiceScopeFactory
{
    private int count;
    public int Count => Volatile.Read(ref count);
    public IServiceScope CreateScope()
    {
        Interlocked.Increment(ref count);
        // A dependency timeout is a retryable error, rather than host cancellation.
        throw new OperationCanceledException("Simulated dependency timeout");
    }
}
sealed class ProbeProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ProbeLogger();
    public void Dispose() { }
    private sealed class ProbeLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
