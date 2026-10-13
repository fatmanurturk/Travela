using Microsoft.EntityFrameworkCore;
using Travela.API.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TravelaDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("TravelaFrontend");

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");


app.MapGet("/api/test", () =>
{
    return Results.Ok(new
    {
        success = true,
        message = "Travela API başarıyla çalışıyor!"
    });
});
app.MapGet("/api/db-test", async (TravelaDbContext db) =>
{
    try
    {
        bool connected = await db.Database.CanConnectAsync();

        if (connected)
        {
            return Results.Ok(new
            {
                success = true,
                message = "TravelaDB bağlantısı başarılı!"
            });
        }

        return Results.Problem(
            "Veritabanına bağlantı kurulamadı.");
    }
    catch
    {
        return Results.Problem(
            "Veritabanı bağlantısı sırasında hata oluştu.");
    }
});

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
