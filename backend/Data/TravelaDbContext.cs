using Microsoft.EntityFrameworkCore;
using Travela.Models;

namespace Travela.Data
{
    public class TravelaDbContext : DbContext
    {
        public TravelaDbContext(
            DbContextOptions<TravelaDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
    }
}