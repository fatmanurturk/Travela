using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Travela.Data;

// Migration tooling does not need to start the API or load JWT credentials.
public class TravelaDbContextFactory : IDesignTimeDbContextFactory<TravelaDbContext>
{
    public TravelaDbContext CreateDbContext(string[] args)
    {
        var directory = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"))
            ? Directory.GetCurrentDirectory() : Path.Combine(Directory.GetCurrentDirectory(), "backend");
        var configuration = new ConfigurationBuilder().SetBasePath(directory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Veritabanı bağlantısı bulunamadı.");
        return new TravelaDbContext(new DbContextOptionsBuilder<TravelaDbContext>().UseSqlServer(connection).Options);
    }
}
