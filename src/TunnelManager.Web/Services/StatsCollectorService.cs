using Logger_MM.Agent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TunnelManager.Web.Data;
using TunnelManager.Web.Models;

namespace TunnelManager.Web.Services;

public class StatsCollectorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StatsCollectorService> _logger;
    private readonly LoggerMMAgent? _loggerMM;
    private DateTime _lastReadTime = DateTime.UtcNow;

    public StatsCollectorService(IServiceProvider serviceProvider, ILogger<StatsCollectorService> logger, LoggerMMAgent? loggerMM = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _loggerMM = loggerMM;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _loggerMM?.Info("StatsCollector", "ExecuteAsync", "Stats collector service started",
            tags: new[] { "stats", "service" });
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectStats();
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting stats");
                _loggerMM?.Error("StatsCollector", "ExecuteAsync", $"Error collecting stats: {ex.Message}",
                    exception: ex,
                    tags: new[] { "stats", "service", "error" });
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
        
        _loggerMM?.Info("StatsCollector", "ExecuteAsync", "Stats collector service stopped",
            tags: new[] { "stats", "service" });
    }

    private async Task CollectStats()
    {
        _loggerMM?.Info("StatsCollector", "CollectStats", "Starting stats collection cycle",
            tags: new[] { "stats", "collection" });
        
        using var scope = _serviceProvider.CreateScope();
        var sshService = scope.ServiceProvider.GetRequiredService<SshService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            var logPath = "/var/log/nginx/access.log";
            var sinceTime = _lastReadTime.ToString("dd/MMM/yyyy:HH:mm:ss");

            // Read new log entries since last read
            var command = $"awk '$4 > \"[{sinceTime}\" {{print}}' {logPath} 2>/dev/null || tail -n 1000 {logPath}";
            var logContent = sshService.ExecuteCommand(command);

            var stats = ParseLogs(logContent);
            _lastReadTime = DateTime.UtcNow;
            
            _loggerMM?.Debug("StatsCollector", "CollectStats", $"Parsed {stats.Count} log entries",
                @params: new { logEntryCount = stats.Count },
                tags: new[] { "stats", "collection" });

            // Group by domain and time window (hourly)
            var grouped = stats
                .GroupBy(s => new { s.Domain, Hour = new DateTime(s.Timestamp.Year, s.Timestamp.Month, s.Timestamp.Day, s.Timestamp.Hour, 0, 0) })
                .Select(g => new TrafficStats
                {
                    Domain = g.Key.Domain,
                    Timestamp = g.Key.Hour,
                    Requests = g.Count(),
                    BytesSent = g.Sum(s => s.BytesSent),
                    BytesReceived = g.Sum(s => s.BytesReceived),
                    UniqueIps = g.Select(s => s.Domain).Distinct().Count(), // Simplified
                    Status2xx = g.Count(s => s.Status2xx > 0),
                    Status4xx = g.Count(s => s.Status4xx > 0),
                    Status5xx = g.Count(s => s.Status5xx > 0)
                })
                .ToList();

            // Save to database
            foreach (var stat in grouped)
            {
                var existing = await dbContext.TrafficStats
                    .FirstOrDefaultAsync(s => s.Domain == stat.Domain && s.Timestamp == stat.Timestamp);

                if (existing != null)
                {
                    existing.Requests += stat.Requests;
                    existing.BytesSent += stat.BytesSent;
                    existing.BytesReceived += stat.BytesReceived;
                    existing.Status2xx += stat.Status2xx;
                    existing.Status4xx += stat.Status4xx;
                    existing.Status5xx += stat.Status5xx;
                }
                else
                {
                    dbContext.TrafficStats.Add(stat);
                }
            }

            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Collected stats for {Count} domains", grouped.Count);
            
            _loggerMM?.Info("StatsCollector", "CollectStats", $"Stats collection completed for {grouped.Count} domains",
                @params: new { domainCount = grouped.Count, totalRequests = grouped.Sum(g => g.Requests) },
                tags: new[] { "stats", "collection" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect stats");
            _loggerMM?.Error("StatsCollector", "CollectStats", $"Failed to collect stats: {ex.Message}",
                exception: ex,
                tags: new[] { "stats", "collection", "error" });
        }
    }

    private List<TrafficStats> ParseLogs(string logContent)
    {
        var stats = new List<TrafficStats>();
        var lines = logContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            try
            {
                // Nginx log format: $remote_addr - $remote_user [$time_local] "$request" $status $body_bytes_sent "$http_referer" "$http_user_agent" "$server_name"
                var match = Regex.Match(line, @"^(\S+)\s+-\s+\S+\s+\[([^\]]+)\]\s+""([^""]+)""\s+(\d+)\s+(\d+)\s+""[^""]*""\s+""[^""]*""\s+""([^""]+)""");

                if (match.Success)
                {
                    var domain = match.Groups[6].Value;
                    var status = int.Parse(match.Groups[4].Value);
                    var bytesSent = long.Parse(match.Groups[5].Value);
                    var timeStr = match.Groups[2].Value;

                    if (DateTime.TryParseExact(timeStr, "dd/MMM/yyyy:HH:mm:ss zzz", null, System.Globalization.DateTimeStyles.None, out var timestamp))
                    {
                        stats.Add(new TrafficStats
                        {
                            Domain = domain,
                            Timestamp = timestamp.ToUniversalTime(),
                            Requests = 1,
                            BytesSent = bytesSent,
                            BytesReceived = 0, // Not in standard log format
                            Status2xx = status >= 200 && status < 300 ? 1 : 0,
                            Status4xx = status >= 400 && status < 500 ? 1 : 0,
                            Status5xx = status >= 500 ? 1 : 0
                        });
                    }
                }
            }
            catch
            {
                // Skip malformed lines
            }
        }

        return stats;
    }
}
