using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Travela.Api.Authorization;
using Travela.Data;

var builder = WebApplication.CreateBuilder(args);
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
});

builder.Services.AddControllers();

builder.Services.AddDbContext<TravelaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("TravelaFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
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
        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(userIdClaim, out var userId))
        {
            var db = context.RequestServices.GetRequiredService<TravelaDbContext>();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Kullanıcı bulunamadı." });
                return;
            }

            var tokenRole = context.User.FindFirstValue(ClaimTypes.Role);
            var tokenVersionClaim = context.User.FindFirstValue("security_version");
            var tokenVersion = int.TryParse(tokenVersionClaim, out var version) ? version : -1;

            if (!user.IsActive || user.Role != tokenRole || user.SecurityVersion != tokenVersion)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Hesabınız pasif veya oturumunuz geçersiz kılındı." });
                return;
            }
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
