using Logger_MM.Agent;
using System.Collections.Concurrent;
using System.Net;
using TunnelManager.Web.Models;

namespace TunnelManager.Web.Services;

public class HealthCheckService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HealthCheckService> _logger;
    private readonly LoggerMMAgent? _loggerMM;
    private readonly ConcurrentDictionary<string, ForwardHealthStatus> _statuses = new();
    
    public event Action? OnStatusChanged;

    public HealthCheckService(IServiceProvider serviceProvider, ILogger<HealthCheckService> logger, LoggerMMAgent? loggerMM = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _loggerMM = loggerMM;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _loggerMM?.Info("HealthCheckService", "ExecuteAsync", "Health check service started",
            tags: new[] { "health", "service" });

        // Wait a bit for services to initialize
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformHealthChecks();
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing health checks");
                _loggerMM?.Error("HealthCheckService", "ExecuteAsync", $"Error performing health checks: {ex.Message}",
                    exception: ex,
                    tags: new[] { "health", "service", "error" });
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _loggerMM?.Info("HealthCheckService", "ExecuteAsync", "Health check service stopped",
            tags: new[] { "health", "service" });
    }

    private async Task PerformHealthChecks()
    {
        using var scope = _serviceProvider.CreateScope();
        var nginxService = scope.ServiceProvider.GetRequiredService<NginxService>();
        var sshService = scope.ServiceProvider.GetRequiredService<SshService>();

        var forwards = nginxService.GetAllForwards();
        
        _loggerMM?.Debug("HealthCheckService", "PerformHealthChecks", $"Checking {forwards.Count} forwards",
            @params: new { forwardCount = forwards.Count },
            tags: new[] { "health", "check" });

        // Perform DNS checks in parallel
        var dnsTasks = forwards.Select(async forward =>
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(forward.Domain);
                var dnsOk = addresses.Length > 0;
                
                UpdateStatus(forward.Domain, dnsOk: dnsOk);
                
                _loggerMM?.Debug("HealthCheckService", "PerformHealthChecks", $"DNS check for {forward.Domain}: {(dnsOk ? "OK" : "FAIL")}",
                    @params: new { domain = forward.Domain, dnsOk, addressCount = addresses.Length },
                    tags: new[] { "health", "dns" });
            }
            catch (Exception ex)
            {
                UpdateStatus(forward.Domain, dnsOk: false);
                _loggerMM?.Warning("HealthCheckService", "PerformHealthChecks", $"DNS check failed for {forward.Domain}: {ex.Message}",
                    @params: new { domain = forward.Domain },
                    tags: new[] { "health", "dns", "error" });
            }
        });

        await Task.WhenAll(dnsTasks);

        // Perform local TCP checks via SSH (batched into single command)
        if (forwards.Any())
        {
            try
            {
                // Build a bash script that checks all targets
                var checkCommands = forwards
                    .Where(f => !string.IsNullOrEmpty(f.Target))
                    .Select(f =>
                    {
                        var parts = f.Target.Split(':');
                        if (parts.Length != 2) return null;
                        var ip = parts[0];
                        var port = parts[1];
                        return $"if timeout 2 bash -c 'echo > /dev/tcp/{ip}/{port}' 2>/dev/null; then echo '{f.Domain}:OK'; else echo '{f.Domain}:FAIL'; fi";
                    })
                    .Where(cmd => cmd != null)
                    .ToList();

                if (checkCommands.Any())
                {
                    var batchScript = string.Join("; ", checkCommands);
                    var result = sshService.ExecuteCommand(batchScript);
                    
                    // Parse results: "domain1:OK", "domain2:FAIL", etc.
                    var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var line in lines)
                    {
                        // Split on last colon to handle domains that might have colons (unlikely but safe)
                        var lastColonIndex = line.LastIndexOf(':');
                        if (lastColonIndex > 0 && lastColonIndex < line.Length - 1)
                        {
                            var domain = line.Substring(0, lastColonIndex);
                            var status = line.Substring(lastColonIndex + 1).Trim();
                            var localOk = status == "OK";
                            
                            UpdateStatus(domain, localOk: localOk);
                            
                            _loggerMM?.Debug("HealthCheckService", "PerformHealthChecks", $"Local check for {domain}: {status}",
                                @params: new { domain, localOk },
                                tags: new[] { "health", "local" });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to perform local TCP checks");
                _loggerMM?.Error("HealthCheckService", "PerformHealthChecks", $"Failed to perform local TCP checks: {ex.Message}",
                    exception: ex,
                    tags: new[] { "health", "local", "error" });
                
                // Mark all as failed if SSH check fails
                foreach (var forward in forwards)
                {
                    UpdateStatus(forward.Domain, localOk: false);
                }
            }
        }

        // Notify subscribers that statuses have changed
        OnStatusChanged?.Invoke();
    }

    private void UpdateStatus(string domain, bool? dnsOk = null, bool? localOk = null)
    {
        _statuses.AddOrUpdate(domain, 
            new ForwardHealthStatus 
            { 
                Domain = domain, 
                DnsOk = dnsOk, 
                LocalOk = localOk,
                LastChecked = DateTime.UtcNow
            },
            (key, existing) =>
            {
                if (dnsOk.HasValue) existing.DnsOk = dnsOk.Value;
                if (localOk.HasValue) existing.LocalOk = localOk.Value;
                existing.LastChecked = DateTime.UtcNow;
                return existing;
            });
    }

    public ForwardHealthStatus? GetStatus(string domain)
    {
        return _statuses.TryGetValue(domain, out var status) ? status : null;
    }

    public Dictionary<string, ForwardHealthStatus> GetAllStatuses()
    {
        return new Dictionary<string, ForwardHealthStatus>(_statuses);
    }
}
