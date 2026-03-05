using Microsoft.EntityFrameworkCore;
using TunnelManager.Web.Models;

namespace TunnelManager.Web.Data;

public class StatsDbContext : DbContext
{
    public StatsDbContext(DbContextOptions<StatsDbContext> options) : base(options)
    {
    }

    public DbSet<TrafficStatsHourly> TrafficStatsHourly { get; set; }
    public DbSet<TrafficRequestLog> TrafficRequestLogs { get; set; }
    public DbSet<DomainDailySummary> DomainDailySummaries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TrafficStatsHourly configuration
        modelBuilder.Entity<TrafficStatsHourly>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Domain, e.Timestamp });
            entity.HasIndex(e => e.Timestamp);
            entity.Property(e => e.Domain).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
        });

        // TrafficRequestLog configuration
        modelBuilder.Entity<TrafficRequestLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Domain, e.Timestamp });
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.RemoteIp);
            entity.Property(e => e.Domain).IsRequired().HasMaxLength(255);
            entity.Property(e => e.RemoteIp).HasMaxLength(45);
            entity.Property(e => e.RequestMethod).HasMaxLength(10);
            entity.Property(e => e.RequestPath).HasMaxLength(2048);
            entity.Property(e => e.UserAgent).HasMaxLength(512);
            entity.Property(e => e.Referer).HasMaxLength(512);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
        });

        // DomainDailySummary configuration
        modelBuilder.Entity<DomainDailySummary>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Domain, e.Date }).IsUnique();
            entity.HasIndex(e => e.Date);
            entity.Property(e => e.Domain).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
        });
    }
}
