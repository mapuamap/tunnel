using System.Text.RegularExpressions;
using TunnelManager.Web.Models;

namespace TunnelManager.Web.Services;

public class SshTunnelService
{
    private readonly SshService _sshService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SshTunnelService> _logger;

    public SshTunnelService(SshService sshService, IConfiguration configuration, ILogger<SshTunnelService> logger)
    {
        _sshService = sshService;
        _configuration = configuration;
        _logger = logger;
    }

    private string StreamDir => _configuration["Vps:NginxStreamDir"] ?? "/etc/nginx/stream.d";
    private int MinPort => int.Parse(_configuration["Vps:SshTunnelPortRange:Min"] ?? "2222");
    private int MaxPort => int.Parse(_configuration["Vps:SshTunnelPortRange:Max"] ?? "2299");

    public List<SshTunnelConfig> GetAllTunnels()
    {
        var tunnels = new List<SshTunnelConfig>();

        try
        {
            // Ensure stream.d directory exists
            _sshService.CreateDirectory(StreamDir);

            var result = _sshService.ExecuteCommand($"ls {StreamDir}/ 2>/dev/null || echo ''");
            var files = result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(f => f.EndsWith(".conf"))
                .ToList();

            foreach (var file in files)
            {
                try
                {
                    var config = GetTunnelConfig(file);
                    if (config != null)
                    {
                        tunnels.Add(config);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse tunnel config for {File}", file);
                }
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
        return ParseConfig(content, name);
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
        // Ensure stream.d directory exists
        _sshService.CreateDirectory(StreamDir);

        var configPath = $"{StreamDir}/ssh-{config.Name}.conf";

        var nginxConfig = $@"server {{
    listen {config.ExternalPort};
    proxy_pass {config.TargetIp}:{config.TargetPort};
    proxy_timeout 600s;
    proxy_connect_timeout 10s;
}}";

        _sshService.WriteFile(configPath, nginxConfig);

        // Open UFW port
        _sshService.ExecuteCommand($"ufw allow {config.ExternalPort}/tcp");

        // Ensure nginx stream module is configured (one-time setup)
        EnsureStreamModuleConfigured();

        // Test and reload
        TestAndReloadNginx();
    }

    public void UpdateTunnel(string name, SshTunnelConfig config)
    {
        var configPath = $"{StreamDir}/ssh-{name}.conf";
        if (!_sshService.FileExists(configPath))
        {
            throw new FileNotFoundException($"Tunnel not found: {name}");
        }

        var existingConfig = GetTunnelConfig(name);
        if (existingConfig == null)
        {
            throw new Exception($"Failed to read existing tunnel config");
        }

        // If port changed, update UFW
        if (existingConfig.ExternalPort != config.ExternalPort)
        {
            _sshService.ExecuteCommand($"ufw delete allow {existingConfig.ExternalPort}/tcp");
            _sshService.ExecuteCommand($"ufw allow {config.ExternalPort}/tcp");
        }

        var nginxConfig = $@"server {{
    listen {config.ExternalPort};
    proxy_pass {config.TargetIp}:{config.TargetPort};
    proxy_timeout 600s;
    proxy_connect_timeout 10s;
}}";

        _sshService.WriteFile(configPath, nginxConfig);
        TestAndReloadNginx();
    }

    public void DeleteTunnel(string name)
    {
        var configPath = $"{StreamDir}/ssh-{name}.conf";
        if (!_sshService.FileExists(configPath))
        {
            return;
        }

        var config = GetTunnelConfig(name);
        if (config != null)
        {
            // Close UFW port
            _sshService.ExecuteCommand($"ufw delete allow {config.ExternalPort}/tcp");
        }

        _sshService.DeleteFile(configPath);
        TestAndReloadNginx();
    }

    private void EnsureStreamModuleConfigured()
    {
        // Check if stream block exists in nginx.conf
        var nginxConfPath = "/etc/nginx/nginx.conf";
        var nginxConf = _sshService.ReadFile(nginxConfPath);

        if (!nginxConf.Contains("stream {"))
        {
            // Add stream module load and stream block
            var streamModuleLoad = "load_module /usr/lib/nginx/modules/ngx_stream_module.so;";
            var streamBlock = @"
stream {
    include /etc/nginx/stream.d/*.conf;
}";

            // Insert after http block
            if (nginxConf.Contains("http {"))
            {
                nginxConf = nginxConf.Replace("http {", $"{streamModuleLoad}\n\nhttp {{");
                nginxConf += streamBlock;
            }
            else
            {
                nginxConf = streamModuleLoad + "\n" + nginxConf + streamBlock;
            }

            _sshService.WriteFile(nginxConfPath, nginxConf);
        }
    }

    private void TestAndReloadNginx()
    {
        var testResult = _sshService.ExecuteCommand("nginx -t");
        if (!testResult.Contains("successful"))
        {
            throw new Exception($"Nginx test failed: {testResult}");
        }
        _sshService.ExecuteCommand("systemctl reload nginx");
    }
}
