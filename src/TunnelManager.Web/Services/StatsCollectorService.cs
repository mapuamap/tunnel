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
        var statsDbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();

        try
        {
            var logPath = "/var/log/nginx/access.log";
            var sinceTime = _lastReadTime.ToString("dd/MMM/yyyy:HH:mm:ss");

            // Read new log entries since last read
            var command = $"awk '$4 > \"[{sinceTime}\" {{print}}' {logPath} 2>/dev/null || tail -n 1000 {logPath}";
            var logContent = sshService.ExecuteCommand(command);

            if (string.IsNullOrWhiteSpace(logContent))
            {
                _loggerMM?.Debug("StatsCollector", "CollectStats", "No new log entries found",
                    tags: new[] { "stats", "collection" });
                return;
            }

            var requestLogs = ParseLogs(logContent);
            _lastReadTime = DateTime.UtcNow;
            
            _loggerMM?.Debug("StatsCollector", "CollectStats", $"Parsed {requestLogs.Count} log entries",
                @params: new { logEntryCount = requestLogs.Count },
                tags: new[] { "stats", "collection" });

            if (requestLogs.Count == 0)
            {
                return;
            }

            // Store individual request logs (keep last 7 days worth)
            var cutoffDate = DateTime.UtcNow.AddDays(-7);
            await statsDbContext.TrafficRequestLogs.AddRangeAsync(requestLogs);
            
            // Delete old request logs
            var oldLogs = await statsDbContext.TrafficRequestLogs
                .Where(l => l.Timestamp < cutoffDate)
                .ToListAsync();
            if (oldLogs.Any())
            {
                statsDbContext.TrafficRequestLogs.RemoveRange(oldLogs);
            }

            // Group by domain and hour for hourly aggregates
            var hourlyGroups = requestLogs
                .GroupBy(r => new 
                { 
                    r.Domain, 
                    Hour = new DateTime(r.Timestamp.Year, r.Timestamp.Month, r.Timestamp.Day, r.Timestamp.Hour, 0, 0, DateTimeKind.Utc)
                })
                .ToList();

            foreach (var group in hourlyGroups)
            {
                var hourlyStat = await statsDbContext.TrafficStatsHourly
                    .FirstOrDefaultAsync(s => s.Domain == group.Key.Domain && s.Timestamp == group.Key.Hour);

                var uniqueIps = group.Select(r => r.RemoteIp).Distinct().Count();
                var status2xx = group.Count(r => r.StatusCode >= 200 && r.StatusCode < 300);
                var status3xx = group.Count(r => r.StatusCode >= 300 && r.StatusCode < 400);
                var status4xx = group.Count(r => r.StatusCode >= 400 && r.StatusCode < 500);
                var status5xx = group.Count(r => r.StatusCode >= 500);
                var avgResponseTime = group.Where(r => r.ResponseTimeMs.HasValue).Select(r => r.ResponseTimeMs!.Value).DefaultIfEmpty(0).Average();

                if (hourlyStat != null)
                {
                    hourlyStat.Requests += group.Count();
                    hourlyStat.BytesSent += group.Sum(r => r.BytesSent);
                    hourlyStat.BytesReceived += group.Sum(r => r.BytesReceived);
                    hourlyStat.UniqueIps = Math.Max(hourlyStat.UniqueIps, uniqueIps);
                    hourlyStat.Status2xx += status2xx;
                    hourlyStat.Status3xx += status3xx;
                    hourlyStat.Status4xx += status4xx;
                    hourlyStat.Status5xx += status5xx;
                    if (avgResponseTime > 0)
                    {
                        hourlyStat.AvgResponseTimeMs = hourlyStat.AvgResponseTimeMs.HasValue
                            ? (hourlyStat.AvgResponseTimeMs + avgResponseTime) / 2
                            : avgResponseTime;
                    }
                }
                else
                {
                    statsDbContext.TrafficStatsHourly.Add(new TrafficStatsHourly
                    {
                        Domain = group.Key.Domain,
                        Timestamp = group.Key.Hour,
                        Requests = group.Count(),
                        BytesSent = group.Sum(r => r.BytesSent),
                        BytesReceived = group.Sum(r => r.BytesReceived),
                        UniqueIps = uniqueIps,
                        Status2xx = status2xx,
                        Status3xx = status3xx,
                        Status4xx = status4xx,
                        Status5xx = status5xx,
                        AvgResponseTimeMs = avgResponseTime > 0 ? avgResponseTime : null
                    });
                }
            }

            // Update daily summaries
            var dailyGroups = requestLogs
                .GroupBy(r => new 
                { 
                    r.Domain, 
                    Date = new DateTime(r.Timestamp.Year, r.Timestamp.Month, r.Timestamp.Day, 0, 0, 0, DateTimeKind.Utc)
                })
                .ToList();

            foreach (var group in dailyGroups)
            {
                var dailySummary = await statsDbContext.DomainDailySummaries
                    .FirstOrDefaultAsync(s => s.Domain == group.Key.Domain && s.Date == group.Key.Date);

                var uniqueIps = group.Select(r => r.RemoteIp).Distinct().Count();
                var status2xx = group.Count(r => r.StatusCode >= 200 && r.StatusCode < 300);
                var status3xx = group.Count(r => r.StatusCode >= 300 && r.StatusCode < 400);
                var status4xx = group.Count(r => r.StatusCode >= 400 && r.StatusCode < 500);
                var status5xx = group.Count(r => r.StatusCode >= 500);
                var avgResponseTime = group.Where(r => r.ResponseTimeMs.HasValue).Select(r => r.ResponseTimeMs!.Value).DefaultIfEmpty(0).Average();

                if (dailySummary != null)
                {
                    dailySummary.TotalRequests += group.Count();
                    dailySummary.TotalBytesSent += group.Sum(r => r.BytesSent);
                    dailySummary.TotalBytesReceived += group.Sum(r => r.BytesReceived);
                    dailySummary.UniqueIps = Math.Max(dailySummary.UniqueIps, uniqueIps);
                    dailySummary.Status2xx += status2xx;
                    dailySummary.Status3xx += status3xx;
                    dailySummary.Status4xx += status4xx;
                    dailySummary.Status5xx += status5xx;
                    if (avgResponseTime > 0)
                    {
                        dailySummary.AvgResponseTimeMs = dailySummary.AvgResponseTimeMs.HasValue
                            ? (dailySummary.AvgResponseTimeMs + avgResponseTime) / 2
                            : avgResponseTime;
                    }
                }
                else
                {
                    statsDbContext.DomainDailySummaries.Add(new DomainDailySummary
                    {
                        Domain = group.Key.Domain,
                        Date = group.Key.Date,
                        TotalRequests = group.Count(),
                        TotalBytesSent = group.Sum(r => r.BytesSent),
                        TotalBytesReceived = group.Sum(r => r.BytesReceived),
                        UniqueIps = uniqueIps,
                        Status2xx = status2xx,
                        Status3xx = status3xx,
                        Status4xx = status4xx,
                        Status5xx = status5xx,
                        AvgResponseTimeMs = avgResponseTime > 0 ? avgResponseTime : null
                    });
                }
            }

            await statsDbContext.SaveChangesAsync();
            _logger.LogInformation("Collected stats: {RequestCount} requests, {DomainCount} domains", 
                requestLogs.Count, hourlyGroups.Select(g => g.Key.Domain).Distinct().Count());
            
            _loggerMM?.Info("StatsCollector", "CollectStats", 
                $"Stats collection completed: {requestLogs.Count} requests, {hourlyGroups.Select(g => g.Key.Domain).Distinct().Count()} domains",
                @params: new { 
                    requestCount = requestLogs.Count, 
                    domainCount = hourlyGroups.Select(g => g.Key.Domain).Distinct().Count(),
                    hourlyGroups = hourlyGroups.Count,
                    dailyGroups = dailyGroups.Count
                },
                tags: new[] { "stats", "collection" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect stats");
            _loggerMM?.Error("StatsCollector", "CollectStats", $"Failed to collect stats: {ex.Message}",
                exception: ex,
                tags: new[] { "stats", "collection", "error" });
            throw;
        }
    }

    private List<TrafficRequestLog> ParseLogs(string logContent)
    {
        var logs = new List<TrafficRequestLog>();
        var lines = logContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Nginx log format: $remote_addr - $remote_user [$time_local] "$request" $status $body_bytes_sent "$http_referer" "$http_user_agent" "$server_name"
        // Extended format may include: $request_time, $upstream_response_time
        var pattern = @"^(\S+)\s+-\s+(\S+)\s+\[([^\]]+)\]\s+""([^""]+)""\s+(\d+)\s+(\d+)\s+""([^""]*)""\s+""([^""]*)""\s+""([^""]+)""(?:\s+([\d.]+))?";

        foreach (var line in lines)
        {
            try
            {
                var match = Regex.Match(line, pattern);
                if (!match.Success)
                    continue;

                var remoteIp = match.Groups[1].Value;
                var timeStr = match.Groups[3].Value;
                var request = match.Groups[4].Value;
                var status = int.Parse(match.Groups[5].Value);
                var bytesSent = long.Parse(match.Groups[6].Value);
                var referer = match.Groups[7].Value;
                var userAgent = match.Groups[8].Value;
                var domain = match.Groups[9].Value;
                var responseTimeStr = match.Groups[10].Success ? match.Groups[10].Value : null;

                if (!DateTime.TryParseExact(timeStr, "dd/MMM/yyyy:HH:mm:ss zzz", null, 
                    System.Globalization.DateTimeStyles.None, out var timestamp))
                {
                    continue;
                }

                // Parse request: "METHOD /path HTTP/1.1"
                var requestParts = request.Split(' ', 3);
                var method = requestParts.Length > 0 ? requestParts[0] : "UNKNOWN";
                var path = requestParts.Length > 1 ? requestParts[1] : "/";

                double? responseTimeMs = null;
                if (!string.IsNullOrEmpty(responseTimeStr) && double.TryParse(responseTimeStr, out var responseTime))
                {
                    responseTimeMs = responseTime * 1000; // Convert seconds to milliseconds
                }

                logs.Add(new TrafficRequestLog
                {
                    Domain = domain,
                    RemoteIp = remoteIp,
                    RequestMethod = method,
                    RequestPath = path,
                    StatusCode = status,
                    BytesSent = bytesSent,
                    BytesReceived = 0, // Not available in standard nginx log
                    UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
                    Referer = string.IsNullOrWhiteSpace(referer) ? null : referer,
                    ResponseTimeMs = responseTimeMs,
                    Timestamp = timestamp.ToUniversalTime()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse log line: {Line}", line);
            }
        }

        return logs;
    }
}
