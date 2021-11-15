using Microsoft.EntityFrameworkCore;

namespace kland.Db;

public class KlandDbContext : DbContext
{
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Thread> Threads => Set<Thread>();
    public DbSet<Ban> Bans => Set<Ban>();

    public KlandDbContext(DbContextOptions<KlandDbContext> options) : base(options)
    {
    }

}
