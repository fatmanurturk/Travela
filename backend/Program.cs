using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Travela.Api.Authorization;
using Travela.Data;
using Travela.Api.Payments;
using Travela.Api.Documents;
using Travela.Api.Reporting;
using Travela.Api.Trips;
using Travela.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
try
{
    builder.Services.AddPaymentProviders(builder.Configuration, builder.Environment);
}
catch (PaymentConfigurationException error)
{
    Console.Error.WriteLine("[PAYMENTS_STARTUP_REJECTED] " + error.Message);
    Environment.ExitCode = 1;
    return;
}
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT Key bulunamadı.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("JWT Issuer bulunamadı.");

var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("JWT Audience bulunamadı.");

builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("bus.create", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new PermissionRequirement("bus.create"));
    });

    options.AddPolicy("bus.view", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new PermissionRequirement("bus.view"));
    });

    options.AddPolicy("bus.update", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new PermissionRequirement("bus.update"));
    });

    options.AddPolicy("trip.view", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new PermissionRequirement("trip.view"));
    });

    options.AddPolicy("trip.create", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new PermissionRequirement("trip.create"));
    });

    options.AddPolicy("trip.update", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.Requirements.Add(new PermissionRequirement("trip.update"));
    });
});

builder.Services.AddScoped<TripLifecycleService>();
builder.Services.AddScoped<TripLifecycleFilter>();
builder.Services.AddControllers(options => options.Filters.AddService<TripLifecycleFilter>());
if (builder.Configuration["Trips:LifecycleEnabled"] != "false") builder.Services.AddHostedService<TripLifecycleWorker>();
builder.Services.AddSingleton<TicketPdfRenderer>();
builder.Services.AddScoped<CompanyReportingService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDataProtection().SetApplicationName("Travela.AccountEmail");
builder.Services.AddOptions<AccountEmailOptions>().Bind(builder.Configuration.GetSection("AccountEmail"))
    .Validate(options => options.Valid(builder.Environment), "Hesap e-postası veya ön yüz adresi yapılandırması geçersiz.").ValidateOnStart();
builder.Services.AddScoped<AccountEmailService>();
builder.Services.AddOptions<Travela.Api.Notifications.QueueWorkerOptions>()
    .Bind(builder.Configuration.GetSection("AccountEmail:Worker"))
    .Validate(options => options.Valid, "AccountEmail:Worker intervals are invalid.").ValidateOnStart();
builder.Services.AddOptions<Travela.Api.Notifications.TravelNotificationWorkerOptions>()
    .Bind(builder.Configuration.GetSection("Notifications:Worker"))
    .Validate(options => options.Valid && options.ReminderIntervalSeconds is >= 1 and <= 3600,
        "Notifications:Worker intervals are invalid.").ValidateOnStart();
builder.Services.AddScoped<EmailTransport>();
builder.Services.AddOptions<Travela.Api.Notifications.SmsOptions>().Bind(builder.Configuration.GetSection("TravelSms"))
    .Validate(options => options.Valid(builder.Environment), "SMS yapılandırması geçersiz.").ValidateOnStart();
builder.Services.AddHttpClient<Travela.Api.Notifications.SmsTransport>(http => http.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<Travela.Api.Notifications.NotificationDelivery>();
if (builder.Configuration["Notifications:WorkerEnabled"] != "false") builder.Services.AddHostedService<Travela.Api.Notifications.TravelNotificationWorker>();
builder.Services.AddScoped<AccountEmailDelivery>();
builder.Services.AddHostedService<AccountEmailWorker>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new { message = "Çok fazla bağlantı isteği gönderildi. Bir dakika sonra tekrar deneyin." }, cancellationToken: ct);
    };
    options.AddPolicy("account-links", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("AccountEmail:RequestLimitPerMinute", 20),
            Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddScoped<Travela.Api.Security.SessionTokenService>();
builder.Services.AddScoped<PaymentService>();
if (builder.Configuration["Payments:RecoveryEnabled"] != "false") builder.Services.AddHostedService<PaymentRecoveryWorker>();

builder.Services.AddDbContext<TravelaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("TravelaFrontend", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrWhiteSpace(origin)) return false;
            return origin.Contains("localhost") || origin.Contains("127.0.0.1");
        })
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("TravelaFrontend");
app.UseRateLimiter();

app.MapGet("/weatherforecast", () =>
{
    var summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast(
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        )).ToArray();

    return forecast;
}).WithName("GetWeatherForecast");

app.MapGet("/api/test", () => Results.Ok(new { success = true, message = "Travela API başarıyla çalışıyor!" }));

app.MapGet("/api/db-test", async (TravelaDbContext db) =>
{
    try
    {
        bool connected = await db.Database.CanConnectAsync();
        return connected
            ? Results.Ok(new { success = true, message = "TravelaDB bağlantısı başarılı!" })
            : Results.Problem("Veritabanına bağlantı kurulamadı.");
    }
    catch
    {
        return Results.Problem("Veritabanı bağlantısı sırasında hata oluştu.");
    }
});

app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var db = context.RequestServices.GetRequiredService<TravelaDbContext>();
        if (!await db.Users.AsNoTracking().ForSession(context.User, db).AnyAsync(context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Hesabınız veya firmanız erişime kapalı, şifre işlemi bekliyor ya da oturumunuz geçersiz kılındı." });
            return;
        }
    }

    await next();
});
app.UseAuthorization();
app.MapControllers();
app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
