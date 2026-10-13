using Microsoft.EntityFrameworkCore;

namespace Travela.API.Data;

public class TravelaDbContext : DbContext
{
    public TravelaDbContext(
        DbContextOptions<TravelaDbContext> options)
        : base(options)
    {
    }
}