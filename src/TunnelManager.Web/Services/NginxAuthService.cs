using System.Text.RegularExpressions;
using TunnelManager.Web.Models;

namespace TunnelManager.Web.Services;

public class NginxAuthService
{
    private readonly SshService _sshService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NginxAuthService> _logger;

    public NginxAuthService(SshService sshService, IConfiguration configuration, ILogger<NginxAuthService> logger)
    {
        _sshService = sshService;
        _configuration = configuration;
        _logger = logger;
    }

    private string ConfigDir => _configuration["Vps:NginxConfigDir"] ?? "/etc/nginx/sites-available";

    public bool HasAuth(string domain)
    {
        var configPath = $"{ConfigDir}/{domain}";
        if (!_sshService.FileExists(configPath))
        {
            return false;
        }

        var content = _sshService.ReadFile(configPath);
        return Regex.IsMatch(content, @"auth_basic");
    }

    public void AddAuth(string domain, string username, string password, string realm = "Restricted Access")
    {
        var configPath = $"{ConfigDir}/{domain}";
        if (!_sshService.FileExists(configPath))
        {
            throw new FileNotFoundException($"Config not found: {domain}");
        }

        var htpasswdPath = $"/etc/nginx/.htpasswd_{domain}";

        // Install apache2-utils if needed
        _sshService.ExecuteCommand("which htpasswd || (apt-get update && apt-get install -y apache2-utils)");

        // Create or update htpasswd file
        var fileExists = _sshService.FileExists(htpasswdPath);
        var cmd = fileExists
            ? $"htpasswd -b {htpasswdPath} {username} {password}"
            : $"htpasswd -cb {htpasswdPath} {username} {password}";

        _sshService.ExecuteCommand(cmd);
        _sshService.ExecuteCommand($"chmod 644 {htpasswdPath}");
        _sshService.ExecuteCommand($"chown root:root {htpasswdPath}");

        // Add auth to nginx config
        var content = _sshService.ReadFile(configPath);

        // Remove existing auth if present
        content = Regex.Replace(content, @"\s*# Basic HTTP Authentication\s*\n", "");
        content = Regex.Replace(content, @"\s*auth_basic\s+""[^""]*"";\s*\n", "");
        content = Regex.Replace(content, @"\s*auth_basic_user_file\s+[^;]+;\s*\n", "");

        // Add auth directives before proxy_pass
        var authDirectives = $@"
        # Basic HTTP Authentication
        auth_basic ""{realm}"";
        auth_basic_user_file {htpasswdPath};
        
        ";

        content = Regex.Replace(
            content,
            @"(location\s+/\s*\{[^}]*?)(proxy_pass[^;]+;)",
            $"$1{authDirectives}$2",
            RegexOptions.Singleline
        );

        _sshService.WriteFile(configPath, content);
        TestAndReloadNginx();
    }

    public void UpdateAuth(string domain, string username, string password)
    {
        var htpasswdPath = $"/etc/nginx/.htpasswd_{domain}";
        if (!_sshService.FileExists(htpasswdPath))
        {
            throw new FileNotFoundException($"Auth not configured for: {domain}");
        }

        _sshService.ExecuteCommand($"htpasswd -b {htpasswdPath} {username} {password}");
    }

    public void RemoveAuth(string domain)
    {
        var configPath = $"{ConfigDir}/{domain}";
        if (!_sshService.FileExists(configPath))
        {
            return;
        }

        var content = _sshService.ReadFile(configPath);

        // Remove auth directives
        content = Regex.Replace(content, @"\s*# Basic HTTP Authentication\s*\n", "");
        content = Regex.Replace(content, @"\s*auth_basic\s+""[^""]*"";\s*\n", "");
        content = Regex.Replace(content, @"\s*auth_basic_user_file\s+[^;]+;\s*\n", "");

        _sshService.WriteFile(configPath, content);

        // Remove htpasswd file
        var htpasswdPath = $"/etc/nginx/.htpasswd_{domain}";
        _sshService.DeleteFile(htpasswdPath);

        TestAndReloadNginx();
    }

    public List<string> GetAuthUsers(string domain)
    {
        var htpasswdPath = $"/etc/nginx/.htpasswd_{domain}";
        if (!_sshService.FileExists(htpasswdPath))
        {
            return new List<string>();
        }

        var content = _sshService.ReadFile(htpasswdPath);
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(':')[0])
            .ToList();
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
