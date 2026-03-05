using Logger_MM.Agent;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TunnelManager.Web.Models;

namespace TunnelManager.Web.Services;

public class NginxService
{
    private readonly SshService _sshService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NginxService> _logger;
    private readonly LoggerMMAgent? _loggerMM;

    // Cache for forward configs (short TTL to keep UI responsive)
    private List<ForwardConfig>? _cachedForwards;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private readonly object _cacheLock = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(15);

    public NginxService(SshService sshService, IConfiguration configuration, ILogger<NginxService> logger, LoggerMMAgent? loggerMM = null)
    {
        _sshService = sshService;
        _configuration = configuration;
        _logger = logger;
        _loggerMM = loggerMM;
    }

    private string ConfigDir => _configuration["Vps:NginxConfigDir"] ?? "/etc/nginx/sites-available";
    private string EnabledDir => _configuration["Vps:NginxEnabledDir"] ?? "/etc/nginx/sites-enabled";

    /// <summary>
    /// Invalidate cache so the next call re-reads from server.
    /// </summary>
    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedForwards = null;
            _cacheExpiry = DateTime.MinValue;
        }
    }

    public List<ForwardConfig> GetAllForwards()
    {
        // Return cached result if still valid
        lock (_cacheLock)
        {
            if (_cachedForwards != null && DateTime.UtcNow < _cacheExpiry)
            {
                _loggerMM?.Debug("NginxService", "GetAllForwards", "Returning cached forward configurations",
                    @params: new { count = _cachedForwards.Count },
                    tags: new[] { "nginx", "forward", "list", "cache" });
                return new List<ForwardConfig>(_cachedForwards);
            }
        }

        _loggerMM?.Info("NginxService", "GetAllForwards", "Retrieving all forward configurations",
            tags: new[] { "nginx", "forward", "list" });

        var forwards = new List<ForwardConfig>();

        try
        {
            // BATCH: Read all configs + check enabled status in TWO ssh commands instead of N*3 SFTP calls
            // 1. Get list of config files + their content in one command
            var batchResult = _sshService.ExecuteCommand(
                $"for f in {ConfigDir}/*; do [ -f \"$f\" ] && echo \"===FILE===$(basename $f)\" && cat \"$f\"; done");

            // 2. Get list of enabled symlinks in one command
            var enabledResult = _sshService.ExecuteCommand($"ls {EnabledDir}/ 2>/dev/null || echo ''");
            var enabledSet = new HashSet<string>(
                enabledResult.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            _loggerMM?.Debug("NginxService", "GetAllForwards", $"Batch read configs, {enabledSet.Count} enabled",
                @params: new { enabledCount = enabledSet.Count, configDir = ConfigDir },
                tags: new[] { "nginx", "forward", "list" });

            // Parse the batch output
            var sections = batchResult.Split("===FILE===", StringSplitOptions.RemoveEmptyEntries);
            foreach (var section in sections)
            {
                var newlineIdx = section.IndexOf('\n');
                if (newlineIdx < 0) continue;

                var domain = section[..newlineIdx].Trim();
                var content = section[(newlineIdx + 1)..];

                if (string.IsNullOrWhiteSpace(domain) || domain == "default") continue;

                try
                {
                    var config = ParseConfig(content, domain, enabledSet.Contains(domain));
                    if (config != null)
                    {
                        forwards.Add(config);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse config for {File}", domain);
                    _loggerMM?.Warning("NginxService", "GetAllForwards", $"Failed to parse config for {domain}: {ex.Message}",
                        @params: new { file = domain },
                        tags: new[] { "nginx", "forward", "list", "parse-error" });
                }
            }

            _loggerMM?.Info("NginxService", "GetAllForwards", $"Retrieved {forwards.Count} forward configurations",
                @params: new { forwardCount = forwards.Count },
                tags: new[] { "nginx", "forward", "list" });

            // Update cache
            lock (_cacheLock)
            {
                _cachedForwards = new List<ForwardConfig>(forwards);
                _cacheExpiry = DateTime.UtcNow + CacheDuration;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list forwards");
            _loggerMM?.Error("NginxService", "GetAllForwards", $"Failed to list forwards: {ex.Message}",
                exception: ex,
                tags: new[] { "nginx", "forward", "list", "error" });
        }

        return forwards;
    }

    public ForwardConfig? GetForwardConfig(string domain)
    {
        _loggerMM?.Debug("NginxService", "GetForwardConfig", $"Getting forward config for domain: {domain}",
            @params: new { domain },
            tags: new[] { "nginx", "forward" });

        var configPath = $"{ConfigDir}/{domain}";
        if (!_sshService.FileExists(configPath))
        {
            _loggerMM?.Debug("NginxService", "GetForwardConfig", $"Config file not found for domain: {domain}",
                @params: new { domain, configPath },
                tags: new[] { "nginx", "forward" });
            return null;
        }

        var content = _sshService.ReadFile(configPath);
        var enabledPath = $"{EnabledDir}/{domain}";
        var isEnabled = _sshService.FileExists(enabledPath);
        var config = ParseConfig(content, domain, isEnabled);

        _loggerMM?.Debug("NginxService", "GetForwardConfig", $"Retrieved forward config for domain: {domain}",
            @params: new { domain, hasSsl = config?.HasSsl, hasAuth = config?.HasAuth, hasWebSocket = config?.HasWebSocket, isEnabled = config?.IsEnabled },
            tags: new[] { "nginx", "forward" });

        return config;
    }

    private ForwardConfig? ParseConfig(string content, string domain, bool isEnabled)
    {
        var config = new ForwardConfig { Domain = domain };

        // Extract target
        var proxyPassMatch = Regex.Match(content, @"proxy_pass\s+http://(\d+\.\d+\.\d+\.\d+):(\d+);");
        if (proxyPassMatch.Success)
        {
            config.Target = $"{proxyPassMatch.Groups[1].Value}:{proxyPassMatch.Groups[2].Value}";
        }

        // Check SSL
        config.HasSsl = Regex.IsMatch(content, @"listen\s+443");

        // Check Auth
        config.HasAuth = Regex.IsMatch(content, @"auth_basic");

        // Check WebSocket
        config.HasWebSocket = Regex.IsMatch(content, @"proxy_http_version\s+1\.1") &&
                             Regex.IsMatch(content, @"Connection\s+[""]upgrade[""]");

        // Use pre-fetched enabled status instead of SFTP call
        config.IsEnabled = isEnabled;

        return config;
    }

    public void CreateForward(ForwardConfig config, bool getSsl = false)
    {
        var configPath = $"{ConfigDir}/{config.Domain}";
        var enabledPath = $"{EnabledDir}/{config.Domain}";

        var wsConfig = config.HasWebSocket ? @"
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection ""upgrade"";" : "";

        var nginxConfig = $@"server {{
    listen 80;
    server_name {config.Domain};
    
    location / {{
        proxy_pass http://{config.Target};
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Prevent stale cache after redeploy
        proxy_hide_header Cache-Control;
        add_header Cache-Control ""no-cache"";{wsConfig}
    }}
}}";

        _sshService.WriteFile(configPath, nginxConfig);
        _sshService.ExecuteCommand($"ln -sf {configPath} {enabledPath}");

        // Test and reload, rollback on failure
        try
        {
            TestAndReloadNginx();
        }
        catch
        {
            // Rollback: remove the broken config
            _sshService.ExecuteCommand($"rm -f {enabledPath}");
            _sshService.DeleteFile(configPath);
            throw;
        }

        InvalidateCache();

        if (getSsl)
        {
            RequestSsl(config.Domain);
        }
    }

    public void UpdateForward(string domain, ForwardConfig config)
    {
        var configPath = $"{ConfigDir}/{domain}";
        if (!_sshService.FileExists(configPath))
        {
            throw new FileNotFoundException($"Config not found: {domain}");
        }

        var existingContent = _sshService.ReadFile(configPath);
        var hasCertbotSsl = Regex.IsMatch(existingContent, @"managed by Certbot");

        _loggerMM?.Debug("NginxService", "UpdateForward", $"Updating forward for {domain}",
            @params: new { domain, target = config.Target, hasWebSocket = config.HasWebSocket, hasSsl = config.HasSsl, hasCertbotSsl },
            tags: new[] { "nginx", "forward", "update" });

        // Build location block content
        var locationLines = new List<string>
        {
            $"        proxy_pass http://{config.Target};",
            "        proxy_set_header Host $host;",
            "        proxy_set_header X-Real-IP $remote_addr;",
            "        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;",
            "        proxy_set_header X-Forwarded-Proto $scheme;",
            "        # Prevent stale cache after redeploy",
            "        proxy_hide_header Cache-Control;",
            "        add_header Cache-Control \"no-cache\";"
        };

        if (config.HasWebSocket)
        {
            locationLines.Add("        proxy_http_version 1.1;");
            locationLines.Add("        proxy_set_header Upgrade $http_upgrade;");
            locationLines.Add("        proxy_set_header Connection \"upgrade\";");
        }

        // Preserve auth if exists in original
        var authMatch = Regex.Match(existingContent, @"(auth_basic\s+""[^""]*"";)\s*(auth_basic_user_file\s+[^;]+;)");
        if (authMatch.Success)
        {
            locationLines.Add("        " + authMatch.Groups[1].Value);
            locationLines.Add("        " + authMatch.Groups[2].Value);
        }

        var locationContent = string.Join("\n", locationLines);
        string nginxConfig;

        if (hasCertbotSsl && config.HasSsl)
        {
            // Preserve the entire certbot-managed config, only replace the location / block content
            nginxConfig = Regex.Replace(existingContent,
                @"(location\s+/\s*\{)\s*[^}]*(})",
                $"$1\n{locationContent}\n    $2",
                RegexOptions.Singleline);

            _loggerMM?.Debug("NginxService", "UpdateForward", $"Preserved certbot SSL config for {domain}",
                tags: new[] { "nginx", "forward", "update", "ssl-preserved" });
        }
        else
        {
            // Build fresh HTTP-only config
            var wsConfig = config.HasWebSocket ? @"
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection ""upgrade"";" : "";

            var authConfig = "";
            if (authMatch.Success)
            {
                authConfig = $"\n        {authMatch.Groups[1].Value}\n        {authMatch.Groups[2].Value}";
            }

            nginxConfig = $@"server {{
    listen 80;
    server_name {domain};
    
    location / {{
        proxy_pass http://{config.Target};
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Prevent stale cache after redeploy
        proxy_hide_header Cache-Control;
        add_header Cache-Control ""no-cache"";{wsConfig}{authConfig}
    }}
}}";
        }

        _loggerMM?.Debug("NginxService", "UpdateForward", $"Writing nginx config for {domain}",
            @params: new { domain, configLength = nginxConfig.Length },
            input: nginxConfig,
            tags: new[] { "nginx", "forward", "update", "config-write" });

        _sshService.WriteFile(configPath, nginxConfig);

        // Test and reload, rollback on failure
        try
        {
            TestAndReloadNginx();
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("NginxService", "UpdateForward", $"Nginx test failed for {domain}, rolling back: {ex.Message}",
                exception: ex,
                @params: new { domain },
                tags: new[] { "nginx", "forward", "update", "rollback" });
            // Rollback: restore original config
            _sshService.WriteFile(configPath, existingContent);
            TestAndReloadNginx();
            throw;
        }

        InvalidateCache();
    }

    public void DeleteForward(string domain)
    {
        _loggerMM?.Info("NginxService", "DeleteForward", $"Deleting forward configuration for domain: {domain}",
            @params: new { domain },
            tags: new[] { "nginx", "forward", "delete" });

        try
        {
            var configPath = $"{ConfigDir}/{domain}";
            var enabledPath = $"{EnabledDir}/{domain}";

            _sshService.ExecuteCommand($"rm -f {enabledPath}");
            _sshService.DeleteFile(configPath);

            TestAndReloadNginx();
            InvalidateCache();

            _loggerMM?.Info("NginxService", "DeleteForward", $"Forward configuration deleted successfully for domain: {domain}",
                @params: new { domain },
                tags: new[] { "nginx", "forward", "delete" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("NginxService", "DeleteForward", $"Failed to delete forward configuration for domain {domain}: {ex.Message}",
                exception: ex,
                @params: new { domain },
                tags: new[] { "nginx", "forward", "delete", "error" });
            throw;
        }
    }

    public void RequestSsl(string domain)
    {
        _loggerMM?.Info("NginxService", "RequestSsl", $"Requesting SSL certificate for domain: {domain}",
            @params: new { domain },
            tags: new[] { "nginx", "ssl", "certbot" });

        try
        {
            _sshService.ExecuteCommand($"certbot --nginx -d {domain} --non-interactive --agree-tos --email admin@denys.fast");
            InvalidateCache();

            _loggerMM?.Info("NginxService", "RequestSsl", $"SSL certificate requested successfully for domain: {domain}",
                @params: new { domain },
                tags: new[] { "nginx", "ssl", "certbot" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("NginxService", "RequestSsl", $"Failed to request SSL certificate for domain {domain}: {ex.Message}",
                exception: ex,
                @params: new { domain },
                tags: new[] { "nginx", "ssl", "certbot", "error" });
            throw;
        }
    }

    private void TestAndReloadNginx()
    {
        // nginx -t outputs everything to stderr. SSH.NET captures stdout/stderr on separate channels,
        // so "2>&1" does not reliably redirect in RunCommand. Instead, rely on exit code:
        // ExecuteCommand throws if exit status != 0, so if we get past this line, the test passed.
        _sshService.ExecuteCommand("nginx -t");
        _sshService.ExecuteCommand("systemctl reload nginx");
    }
}
