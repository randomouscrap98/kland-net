using Microsoft.EntityFrameworkCore;

namespace kland.Db;

public class KlandDbContext : DbContext
{
    public DbSet<Post>? Posts {get;set;}
    public DbSet<Thread>? Threads {get;set;}
    public DbSet<Ban>? Bans {get;set;}

    public KlandDbContext(DbContextOptions<KlandDbContext> options) : base(options)
    {
    }

}
