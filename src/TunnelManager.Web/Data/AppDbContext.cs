using Microsoft.EntityFrameworkCore;
using TunnelManager.Web.Models;

namespace TunnelManager.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<TrafficStats> TrafficStats { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TrafficStats>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Domain, e.Timestamp });
            entity.Property(e => e.Domain).IsRequired().HasMaxLength(255);
        });
    }
}
