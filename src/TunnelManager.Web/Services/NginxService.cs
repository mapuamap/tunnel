using System.Text.RegularExpressions;
using TunnelManager.Web.Models;

namespace TunnelManager.Web.Services;

public class NginxService
{
    private readonly SshService _sshService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NginxService> _logger;

    public NginxService(SshService sshService, IConfiguration configuration, ILogger<NginxService> logger)
    {
        _sshService = sshService;
        _configuration = configuration;
        _logger = logger;
    }

    private string ConfigDir => _configuration["Vps:NginxConfigDir"] ?? "/etc/nginx/sites-available";
    private string EnabledDir => _configuration["Vps:NginxEnabledDir"] ?? "/etc/nginx/sites-enabled";

    public List<ForwardConfig> GetAllForwards()
    {
        var forwards = new List<ForwardConfig>();
        
        try
        {
            var result = _sshService.ExecuteCommand($"ls {ConfigDir}/");
            var files = result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(f => f.Contains(".denys.fast") || f.Contains("."))
                .ToList();

            foreach (var file in files)
            {
                try
                {
                    var config = GetForwardConfig(file);
                    if (config != null)
                    {
                        forwards.Add(config);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse config for {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list forwards");
        }

        return forwards;
    }

    public ForwardConfig? GetForwardConfig(string domain)
    {
        var configPath = $"{ConfigDir}/{domain}";
        if (!_sshService.FileExists(configPath))
        {
            return null;
        }

        var content = _sshService.ReadFile(configPath);
        return ParseConfig(content, domain);
    }

    private ForwardConfig? ParseConfig(string content, string domain)
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

        // Check if enabled
        var enabledPath = $"{EnabledDir}/{domain}";
        config.IsEnabled = _sshService.FileExists(enabledPath);

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
        proxy_set_header X-Forwarded-Proto $scheme;{wsConfig}
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
        var wsConfig = config.HasWebSocket ? @"
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection ""upgrade"";" : "";

        // Preserve SSL config if exists
        var sslConfig = "";
        if (Regex.IsMatch(existingContent, @"listen\s+443"))
        {
            var sslMatch = Regex.Match(existingContent, @"(listen\s+443[^;]+;[\s\S]*?ssl_certificate[^;]+;[\s\S]*?ssl_certificate_key[^;]+;)");
            if (sslMatch.Success)
            {
                sslConfig = sslMatch.Groups[1].Value + "\n    ";
            }
        }

        // Preserve auth if exists
        var authConfig = "";
        if (Regex.IsMatch(existingContent, @"auth_basic"))
        {
            var authMatch = Regex.Match(existingContent, @"(auth_basic[^;]+;[\s\S]*?auth_basic_user_file[^;]+;)");
            if (authMatch.Success)
            {
                authConfig = "\n        " + authMatch.Groups[1].Value;
            }
        }

        var nginxConfig = $@"server {{
    listen 80;{sslConfig}
    server_name {domain};
    
    location / {{
        proxy_pass http://{config.Target};
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;{wsConfig}{authConfig}
    }}
}}";

        _sshService.WriteFile(configPath, nginxConfig);

        // Test and reload, rollback on failure
        try
        {
            TestAndReloadNginx();
        }
        catch
        {
            // Rollback: restore original config
            _sshService.WriteFile(configPath, existingContent);
            throw;
        }
    }

    public void DeleteForward(string domain)
    {
        var configPath = $"{ConfigDir}/{domain}";
        var enabledPath = $"{EnabledDir}/{domain}";

        _sshService.ExecuteCommand($"rm -f {enabledPath}");
        _sshService.DeleteFile(configPath);

        TestAndReloadNginx();
    }

    public void RequestSsl(string domain)
    {
        _sshService.ExecuteCommand($"certbot --nginx -d {domain} --non-interactive --agree-tos --email admin@denys.fast");
    }

    private void TestAndReloadNginx()
    {
        // nginx -t writes output to stderr, so redirect stderr to stdout with 2>&1
        var testResult = _sshService.ExecuteCommand("nginx -t 2>&1");
        if (!testResult.Contains("successful"))
        {
            throw new Exception($"Nginx test failed:\n{testResult}");
        }
        _sshService.ExecuteCommand("systemctl reload nginx");
    }
}
