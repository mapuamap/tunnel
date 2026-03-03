using Logger_MM.Agent;
using System.Text.RegularExpressions;
using TunnelManager.Web.Models;

namespace TunnelManager.Web.Services;

public class SshTunnelService
{
    private readonly SshService _sshService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SshTunnelService> _logger;
    private readonly LoggerMMAgent? _loggerMM;

    // Cache
    private List<SshTunnelConfig>? _cachedTunnels;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private readonly object _cacheLock = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(15);

    public SshTunnelService(SshService sshService, IConfiguration configuration, ILogger<SshTunnelService> logger, LoggerMMAgent? loggerMM = null)
    {
        _sshService = sshService;
        _configuration = configuration;
        _logger = logger;
        _loggerMM = loggerMM;
    }

    private string StreamDir => _configuration["Vps:NginxStreamDir"] ?? "/etc/nginx/stream.d";
    private int MinPort => int.Parse(_configuration["Vps:SshTunnelPortRange:Min"] ?? "2222");
    private int MaxPort => int.Parse(_configuration["Vps:SshTunnelPortRange:Max"] ?? "2299");

    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedTunnels = null;
            _cacheExpiry = DateTime.MinValue;
        }
    }

    public List<SshTunnelConfig> GetAllTunnels()
    {
        // Return cached result if still valid
        lock (_cacheLock)
        {
            if (_cachedTunnels != null && DateTime.UtcNow < _cacheExpiry)
            {
                return new List<SshTunnelConfig>(_cachedTunnels);
            }
        }

        var tunnels = new List<SshTunnelConfig>();

        try
        {
            // Ensure stream.d directory exists + batch-read all configs in ONE command
            var batchResult = _sshService.ExecuteCommand(
                $"mkdir -p {StreamDir} && for f in {StreamDir}/*.conf; do [ -f \"$f\" ] && echo \"===FILE===$(basename $f)\" && cat \"$f\"; done 2>/dev/null || echo ''");

            if (!string.IsNullOrWhiteSpace(batchResult) && batchResult.Contains("===FILE==="))
            {
                var sections = batchResult.Split("===FILE===", StringSplitOptions.RemoveEmptyEntries);
                foreach (var section in sections)
                {
                    var newlineIdx = section.IndexOf('\n');
                    if (newlineIdx < 0) continue;

                    var fileName = section[..newlineIdx].Trim();
                    var content = section[(newlineIdx + 1)..];

                    if (!fileName.EndsWith(".conf")) continue;

                    try
                    {
                        var config = ParseConfig(content, fileName);
                        if (config != null)
                        {
                            tunnels.Add(config);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse tunnel config for {File}", fileName);
                    }
                }
            }

            // Update cache
            lock (_cacheLock)
            {
                _cachedTunnels = new List<SshTunnelConfig>(tunnels);
                _cacheExpiry = DateTime.UtcNow + CacheDuration;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list tunnels");
        }

        return tunnels;
    }

    public SshTunnelConfig? GetTunnelConfig(string name)
    {
        var configPath = $"{StreamDir}/ssh-{name}.conf";
        if (!_sshService.FileExists(configPath))
        {
            return null;
        }

        var content = _sshService.ReadFile(configPath);
        return ParseConfig(content, $"ssh-{name}.conf");
    }

    private SshTunnelConfig? ParseConfig(string content, string fileName)
    {
        var name = fileName.Replace("ssh-", "").Replace(".conf", "");
        var config = new SshTunnelConfig { Name = name };

        // Extract port
        var listenMatch = Regex.Match(content, @"listen\s+(\d+);");
        if (listenMatch.Success)
        {
            config.ExternalPort = int.Parse(listenMatch.Groups[1].Value);
        }

        // Extract target
        var proxyMatch = Regex.Match(content, @"proxy_pass\s+(\d+\.\d+\.\d+\.\d+):(\d+);");
        if (proxyMatch.Success)
        {
            config.TargetIp = proxyMatch.Groups[1].Value;
            config.TargetPort = int.Parse(proxyMatch.Groups[2].Value);
        }

        config.IsEnabled = true; // If file exists, it's enabled
        return config;
    }

    public int GetNextAvailablePort()
    {
        var existingTunnels = GetAllTunnels();
        var usedPorts = existingTunnels.Select(t => t.ExternalPort).ToHashSet();

        for (int port = MinPort; port <= MaxPort; port++)
        {
            if (!usedPorts.Contains(port))
            {
                return port;
            }
        }

        throw new Exception($"No available ports in range {MinPort}-{MaxPort}");
    }

    public void CreateTunnel(SshTunnelConfig config)
    {
        _loggerMM?.Info("SshTunnelService", "CreateTunnel", $"Creating SSH tunnel: {config.Name}",
            @params: new { name = config.Name, externalPort = config.ExternalPort, targetIp = config.TargetIp, targetPort = config.TargetPort },
            tags: new[] { "tunnel", "create" });
        
        try
        {
            // Ensure stream.d directory exists
            _sshService.CreateDirectory(StreamDir);

            // Ensure nginx stream module is configured BEFORE writing config
            EnsureStreamModuleConfigured();

            var configPath = $"{StreamDir}/ssh-{config.Name}.conf";

            var nginxConfig = $@"server {{
    listen {config.ExternalPort};
    proxy_pass {config.TargetIp}:{config.TargetPort};
    proxy_timeout 600s;
    proxy_connect_timeout 10s;
}}";

            _sshService.WriteFile(configPath, nginxConfig);

            // Open firewall port (try iptables, ignore errors if firewall not available)
            try
            {
                _sshService.ExecuteCommand($"iptables -I INPUT -p tcp --dport {config.ExternalPort} -j ACCEPT");
            }
            catch (Exception fwEx)
            {
                _loggerMM?.Warning("SshTunnelService", "CreateTunnel", $"Could not open firewall port {config.ExternalPort}: {fwEx.Message}",
                    @params: new { name = config.Name, externalPort = config.ExternalPort },
                    tags: new[] { "tunnel", "create", "firewall", "warning" });
            }

            // Test and reload
            try
            {
                TestAndReloadNginx();
            }
            catch
            {
                // Rollback: remove the broken config
                _sshService.DeleteFile(configPath);
                throw;
            }

            InvalidateCache();
            
            _loggerMM?.Info("SshTunnelService", "CreateTunnel", $"SSH tunnel created successfully: {config.Name}",
                @params: new { name = config.Name, externalPort = config.ExternalPort, targetIp = config.TargetIp, targetPort = config.TargetPort },
                tags: new[] { "tunnel", "create" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("SshTunnelService", "CreateTunnel", $"Failed to create SSH tunnel {config.Name}: {ex.Message}",
                exception: ex,
                @params: new { name = config.Name, externalPort = config.ExternalPort, targetIp = config.TargetIp, targetPort = config.TargetPort },
                tags: new[] { "tunnel", "create", "error" });
            throw;
        }
    }

    public void UpdateTunnel(string name, SshTunnelConfig config)
    {
        _loggerMM?.Info("SshTunnelService", "UpdateTunnel", $"Updating SSH tunnel: {name}",
            @params: new { name, externalPort = config.ExternalPort, targetIp = config.TargetIp, targetPort = config.TargetPort },
            tags: new[] { "tunnel", "update" });
        
        try
        {
            var configPath = $"{StreamDir}/ssh-{name}.conf";
            if (!_sshService.FileExists(configPath))
            {
                _loggerMM?.Error("SshTunnelService", "UpdateTunnel", $"Tunnel not found: {name}",
                    @params: new { name },
                    tags: new[] { "tunnel", "update", "error" });
                throw new FileNotFoundException($"Tunnel not found: {name}");
            }

            var existingConfig = GetTunnelConfig(name);
            if (existingConfig == null)
            {
                _loggerMM?.Error("SshTunnelService", "UpdateTunnel", $"Failed to read existing tunnel config: {name}",
                    @params: new { name },
                    tags: new[] { "tunnel", "update", "error" });
                throw new Exception($"Failed to read existing tunnel config");
            }

            // If port changed, update firewall
            if (existingConfig.ExternalPort != config.ExternalPort)
            {
                _loggerMM?.Info("SshTunnelService", "UpdateTunnel", $"Port changed for tunnel {name}, updating firewall rules",
                    @params: new { name, oldPort = existingConfig.ExternalPort, newPort = config.ExternalPort },
                    tags: new[] { "tunnel", "update", "firewall" });
                try
                {
                    _sshService.ExecuteCommand($"iptables -D INPUT -p tcp --dport {existingConfig.ExternalPort} -j ACCEPT 2>/dev/null; iptables -I INPUT -p tcp --dport {config.ExternalPort} -j ACCEPT");
                }
                catch (Exception fwEx)
                {
                    _loggerMM?.Warning("SshTunnelService", "UpdateTunnel", $"Could not update firewall rules: {fwEx.Message}",
                        @params: new { name, oldPort = existingConfig.ExternalPort, newPort = config.ExternalPort },
                        tags: new[] { "tunnel", "update", "firewall", "warning" });
                }
            }

            var nginxConfig = $@"server {{
    listen {config.ExternalPort};
    proxy_pass {config.TargetIp}:{config.TargetPort};
    proxy_timeout 600s;
    proxy_connect_timeout 10s;
}}";

            _sshService.WriteFile(configPath, nginxConfig);
            TestAndReloadNginx();
            InvalidateCache();
            
            _loggerMM?.Info("SshTunnelService", "UpdateTunnel", $"SSH tunnel updated successfully: {name}",
                @params: new { name, externalPort = config.ExternalPort, targetIp = config.TargetIp, targetPort = config.TargetPort },
                tags: new[] { "tunnel", "update" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("SshTunnelService", "UpdateTunnel", $"Failed to update SSH tunnel {name}: {ex.Message}",
                exception: ex,
                @params: new { name },
                tags: new[] { "tunnel", "update", "error" });
            throw;
        }
    }

    public void DeleteTunnel(string name)
    {
        _loggerMM?.Info("SshTunnelService", "DeleteTunnel", $"Deleting SSH tunnel: {name}",
            @params: new { name },
            tags: new[] { "tunnel", "delete" });
        
        try
        {
            var configPath = $"{StreamDir}/ssh-{name}.conf";
            if (!_sshService.FileExists(configPath))
            {
                _loggerMM?.Debug("SshTunnelService", "DeleteTunnel", $"Tunnel config file not found: {name}",
                    @params: new { name },
                    tags: new[] { "tunnel", "delete" });
                return;
            }

            var config = GetTunnelConfig(name);
            if (config != null)
            {
                // Close firewall port
                _loggerMM?.Debug("SshTunnelService", "DeleteTunnel", $"Removing firewall rule for port {config.ExternalPort}",
                    @params: new { name, externalPort = config.ExternalPort },
                    tags: new[] { "tunnel", "delete", "firewall" });
                try
                {
                    _sshService.ExecuteCommand($"iptables -D INPUT -p tcp --dport {config.ExternalPort} -j ACCEPT");
                }
                catch (Exception fwEx)
                {
                    _loggerMM?.Warning("SshTunnelService", "DeleteTunnel", $"Could not remove firewall rule for port {config.ExternalPort}: {fwEx.Message}",
                        @params: new { name, externalPort = config.ExternalPort },
                        tags: new[] { "tunnel", "delete", "firewall", "warning" });
                }
            }

            _sshService.DeleteFile(configPath);
            TestAndReloadNginx();
            InvalidateCache();
            
            _loggerMM?.Info("SshTunnelService", "DeleteTunnel", $"SSH tunnel deleted successfully: {name}",
                @params: new { name },
                tags: new[] { "tunnel", "delete" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("SshTunnelService", "DeleteTunnel", $"Failed to delete SSH tunnel {name}: {ex.Message}",
                exception: ex,
                @params: new { name },
                tags: new[] { "tunnel", "delete", "error" });
            throw;
        }
    }

    private void EnsureStreamModuleConfigured()
    {
        _loggerMM?.Debug("SshTunnelService", "EnsureStreamModuleConfigured", "Checking if nginx stream module is configured",
            tags: new[] { "tunnel", "nginx", "stream" });
        
        try
        {
            var nginxConfPath = "/etc/nginx/nginx.conf";
            var nginxConf = _sshService.ReadFile(nginxConfPath);

            // Check if stream block with include already exists
            if (Regex.IsMatch(nginxConf, @"stream\s*\{[^}]*include\s+/etc/nginx/stream\.d/"))
            {
                _loggerMM?.Debug("SshTunnelService", "EnsureStreamModuleConfigured", "Nginx stream module already configured",
                    tags: new[] { "tunnel", "nginx", "stream" });
                return;
            }

            _loggerMM?.Info("SshTunnelService", "EnsureStreamModuleConfigured", "Configuring nginx stream module",
                tags: new[] { "tunnel", "nginx", "stream" });

            // Remove any bare include of stream.d at top level (broken config)
            nginxConf = Regex.Replace(nginxConf, @"\n?include\s+/etc/nginx/stream\.d/\*\.conf;\n?", "\n");

            // Remove any existing stream block (might be malformed)
            // Only if it doesn't contain our include
            if (nginxConf.Contains("stream {") && !Regex.IsMatch(nginxConf, @"stream\s*\{[^}]*include\s+/etc/nginx/stream\.d/"))
            {
                nginxConf = Regex.Replace(nginxConf, @"stream\s*\{[^}]*\}\s*", "");
            }

            // Add proper stream block at the end
            var streamBlock = @"
stream {
    include /etc/nginx/stream.d/*.conf;
}
";
            nginxConf = nginxConf.TrimEnd() + "\n" + streamBlock;

            _sshService.WriteFile(nginxConfPath, nginxConf);
            
            _loggerMM?.Info("SshTunnelService", "EnsureStreamModuleConfigured", "Nginx stream module configured successfully",
                tags: new[] { "tunnel", "nginx", "stream" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("SshTunnelService", "EnsureStreamModuleConfigured", $"Failed to configure nginx stream module: {ex.Message}",
                exception: ex,
                tags: new[] { "tunnel", "nginx", "stream", "error" });
            throw;
        }
    }

    private void TestAndReloadNginx()
    {
        _loggerMM?.Debug("SshTunnelService", "TestAndReloadNginx", "Testing nginx configuration",
            tags: new[] { "tunnel", "nginx", "reload" });
        
        try
        {
            // nginx -t outputs everything to stderr. SSH.NET captures stdout/stderr on separate channels,
            // so "2>&1" does not reliably redirect in RunCommand. Instead, rely on exit code:
            // ExecuteCommand throws if exit status != 0, so if we get past this line, the test passed.
            _sshService.ExecuteCommand("nginx -t");
            
            _loggerMM?.Debug("SshTunnelService", "TestAndReloadNginx", "Nginx configuration test successful, reloading",
                tags: new[] { "tunnel", "nginx", "reload" });
            
            _sshService.ExecuteCommand("systemctl reload nginx");
            
            _loggerMM?.Info("SshTunnelService", "TestAndReloadNginx", "Nginx reloaded successfully",
                tags: new[] { "tunnel", "nginx", "reload" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("SshTunnelService", "TestAndReloadNginx", $"Failed to test and reload nginx: {ex.Message}",
                exception: ex,
                tags: new[] { "tunnel", "nginx", "reload", "error" });
            throw;
        }
    }
}
