using Logger_MM.Agent;
using System.Text.RegularExpressions;
using TunnelManager.Web.Models;

namespace TunnelManager.Web.Services;

public class NginxAuthService
{
    private readonly SshService _sshService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NginxAuthService> _logger;
    private readonly LoggerMMAgent? _loggerMM;

    public NginxAuthService(SshService sshService, IConfiguration configuration, ILogger<NginxAuthService> logger, LoggerMMAgent? loggerMM = null)
    {
        _sshService = sshService;
        _configuration = configuration;
        _logger = logger;
        _loggerMM = loggerMM;
    }

    private string ConfigDir => _configuration["Vps:NginxConfigDir"] ?? "/etc/nginx/sites-available";

    public bool HasAuth(string domain)
    {
        _loggerMM?.Debug("NginxAuthService", "HasAuth", $"Checking if auth is configured for domain: {domain}",
            @params: new { domain },
            tags: new[] { "nginx", "auth" });
        
        var configPath = $"{ConfigDir}/{domain}";
        if (!_sshService.FileExists(configPath))
        {
            _loggerMM?.Debug("NginxAuthService", "HasAuth", $"Config file not found for domain: {domain}",
                @params: new { domain },
                tags: new[] { "nginx", "auth" });
            return false;
        }

        var content = _sshService.ReadFile(configPath);
        var hasAuth = Regex.IsMatch(content, @"auth_basic");
        
        _loggerMM?.Debug("NginxAuthService", "HasAuth", $"Auth check result for domain {domain}: {hasAuth}",
            @params: new { domain, hasAuth },
            tags: new[] { "nginx", "auth" });
        
        return hasAuth;
    }

    public AuthType GetAuthType(string domain)
    {
        _loggerMM?.Debug("NginxAuthService", "GetAuthType", $"Detecting auth type for domain: {domain}",
            @params: new { domain },
            tags: new[] { "nginx", "auth" });

        var configPath = $"{ConfigDir}/{domain}";
        if (!_sshService.FileExists(configPath))
        {
            _loggerMM?.Debug("NginxAuthService", "GetAuthType", $"Config file not found for domain: {domain}",
                @params: new { domain },
                tags: new[] { "nginx", "auth" });
            return AuthType.None;
        }

        var content = _sshService.ReadFile(configPath);

        if (Regex.IsMatch(content, @"include\s+snippets/sso-auth\.conf"))
            return AuthType.KeycloakSSO;

        if (Regex.IsMatch(content, @"auth_basic"))
            return AuthType.BasicAuth;

        return AuthType.None;
    }

    public void EnableSso(string domain)
    {
        _loggerMM?.Info("NginxAuthService", "EnableSso", $"Enabling Keycloak SSO for domain: {domain}",
            @params: new { domain },
            tags: new[] { "nginx", "auth", "sso", "add" });

        try
        {
            var configPath = $"{ConfigDir}/{domain}";
            if (!_sshService.FileExists(configPath))
                throw new FileNotFoundException($"Config not found: {domain}");

            var content = _sshService.ReadFile(configPath);

            // Remove Basic Auth if present before switching
            content = Regex.Replace(content, @"\s*# Basic HTTP Authentication\s*\n", "");
            content = Regex.Replace(content, @"\s*auth_basic\s+""[^""]*"";\s*\n", "");
            content = Regex.Replace(content, @"\s*auth_basic_user_file\s+[^;]+;\s*\n", "");

            // Add sso-headers inside location / (before proxy_pass)
            content = Regex.Replace(
                content,
                @"(location\s+/\s*\{[^}]*?)(proxy_pass[^;]+;)",
                "$1        include snippets/sso-headers.conf;\n\n        $2",
                RegexOptions.Singleline
            );

            // Add sso-auth.conf include + @sso_redirect before the first location block
            var ssoBlock = @"
    include snippets/sso-auth.conf;
    location @sso_redirect {
        return 302 https://$host/oauth2/sign_in?rd=$scheme://$host$request_uri;
    }

";
            content = Regex.Replace(
                content,
                @"(\s*location\s+/\s*\{)",
                ssoBlock + "$1",
                RegexOptions.Singleline
            );

            _sshService.WriteFile(configPath, content);

            // Remove htpasswd file if it exists
            var htpasswdPath = $"/etc/nginx/.htpasswd_{domain}";
            _sshService.DeleteFile(htpasswdPath);

            TestAndReloadNginx();

            _loggerMM?.Info("NginxAuthService", "EnableSso", $"Keycloak SSO enabled for domain: {domain}",
                @params: new { domain },
                tags: new[] { "nginx", "auth", "sso", "add" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("NginxAuthService", "EnableSso", $"Failed to enable SSO for domain {domain}: {ex.Message}",
                exception: ex,
                @params: new { domain },
                tags: new[] { "nginx", "auth", "sso", "error" });
            throw;
        }
    }

    public void DisableSso(string domain)
    {
        _loggerMM?.Info("NginxAuthService", "DisableSso", $"Disabling Keycloak SSO for domain: {domain}",
            @params: new { domain },
            tags: new[] { "nginx", "auth", "sso", "remove" });

        try
        {
            var configPath = $"{ConfigDir}/{domain}";
            if (!_sshService.FileExists(configPath))
                return;

            var content = _sshService.ReadFile(configPath);

            // Remove include snippets/sso-auth.conf line
            content = Regex.Replace(content, @"\s*include\s+snippets/sso-auth\.conf;\s*\n", "\n");

            // Remove location @sso_redirect { ... } block
            content = Regex.Replace(content, @"\s*location\s+@sso_redirect\s*\{[^}]*\}\s*\n", "\n",
                RegexOptions.Singleline);

            // Remove include snippets/sso-headers.conf line
            content = Regex.Replace(content, @"\s*include\s+snippets/sso-headers\.conf;\s*\n", "\n");

            _sshService.WriteFile(configPath, content);
            TestAndReloadNginx();

            _loggerMM?.Info("NginxAuthService", "DisableSso", $"Keycloak SSO disabled for domain: {domain}",
                @params: new { domain },
                tags: new[] { "nginx", "auth", "sso", "remove" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("NginxAuthService", "DisableSso", $"Failed to disable SSO for domain {domain}: {ex.Message}",
                exception: ex,
                @params: new { domain },
                tags: new[] { "nginx", "auth", "sso", "error" });
            throw;
        }
    }

    public void AddAuth(string domain, string username, string password, string realm = "Restricted Access")
    {
        _loggerMM?.Info("NginxAuthService", "AddAuth", $"Adding Basic Auth for domain: {domain}, username: {username}",
            @params: new { domain, username, realm },
            tags: new[] { "nginx", "auth", "add" });
        
        try
        {
            var configPath = $"{ConfigDir}/{domain}";
            if (!_sshService.FileExists(configPath))
            {
                _loggerMM?.Error("NginxAuthService", "AddAuth", $"Config not found for domain: {domain}",
                    @params: new { domain },
                    tags: new[] { "nginx", "auth", "add", "error" });
                throw new FileNotFoundException($"Config not found: {domain}");
            }

            var htpasswdPath = $"/etc/nginx/.htpasswd_{domain}";

            // Install apache2-utils if needed
            _sshService.ExecuteCommand("which htpasswd || (apt-get update && apt-get install -y apache2-utils)");

            // Create or update htpasswd file (shell-escape username and password to handle special chars)
            var escapedUsername = username.Replace("'", "'\\''");
            var escapedPassword = password.Replace("'", "'\\''");
            var fileExists = _sshService.FileExists(htpasswdPath);
            var cmd = fileExists
                ? $"htpasswd -b {htpasswdPath} '{escapedUsername}' '{escapedPassword}'"
                : $"htpasswd -cb {htpasswdPath} '{escapedUsername}' '{escapedPassword}'";

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
            
            _loggerMM?.Info("NginxAuthService", "AddAuth", $"Basic Auth added successfully for domain: {domain}, username: {username}",
                @params: new { domain, username, realm },
                tags: new[] { "nginx", "auth", "add" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("NginxAuthService", "AddAuth", $"Failed to add Basic Auth for domain {domain}: {ex.Message}",
                exception: ex,
                @params: new { domain, username },
                tags: new[] { "nginx", "auth", "add", "error" });
            throw;
        }
    }

    public void UpdateAuth(string domain, string username, string password)
    {
        _loggerMM?.Info("NginxAuthService", "UpdateAuth", $"Updating Basic Auth password for domain: {domain}, username: {username}",
            @params: new { domain, username },
            tags: new[] { "nginx", "auth", "update" });
        
        try
        {
            var htpasswdPath = $"/etc/nginx/.htpasswd_{domain}";
            if (!_sshService.FileExists(htpasswdPath))
            {
                _loggerMM?.Error("NginxAuthService", "UpdateAuth", $"Auth not configured for domain: {domain}",
                    @params: new { domain },
                    tags: new[] { "nginx", "auth", "update", "error" });
                throw new FileNotFoundException($"Auth not configured for: {domain}");
            }

            var escapedUsername = username.Replace("'", "'\\''");
            var escapedPassword = password.Replace("'", "'\\''");
            _sshService.ExecuteCommand($"htpasswd -b {htpasswdPath} '{escapedUsername}' '{escapedPassword}'");
            
            _loggerMM?.Info("NginxAuthService", "UpdateAuth", $"Basic Auth password updated successfully for domain: {domain}, username: {username}",
                @params: new { domain, username },
                tags: new[] { "nginx", "auth", "update" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("NginxAuthService", "UpdateAuth", $"Failed to update Basic Auth password for domain {domain}: {ex.Message}",
                exception: ex,
                @params: new { domain, username },
                tags: new[] { "nginx", "auth", "update", "error" });
            throw;
        }
    }

    public void RemoveAuth(string domain)
    {
        _loggerMM?.Info("NginxAuthService", "RemoveAuth", $"Removing Basic Auth for domain: {domain}",
            @params: new { domain },
            tags: new[] { "nginx", "auth", "remove" });
        
        try
        {
            var configPath = $"{ConfigDir}/{domain}";
            if (!_sshService.FileExists(configPath))
            {
                _loggerMM?.Debug("NginxAuthService", "RemoveAuth", $"Config file not found for domain: {domain}",
                    @params: new { domain },
                    tags: new[] { "nginx", "auth", "remove" });
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
            
            _loggerMM?.Info("NginxAuthService", "RemoveAuth", $"Basic Auth removed successfully for domain: {domain}",
                @params: new { domain },
                tags: new[] { "nginx", "auth", "remove" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("NginxAuthService", "RemoveAuth", $"Failed to remove Basic Auth for domain {domain}: {ex.Message}",
                exception: ex,
                @params: new { domain },
                tags: new[] { "nginx", "auth", "remove", "error" });
            throw;
        }
    }

    public List<string> GetAuthUsers(string domain)
    {
        _loggerMM?.Debug("NginxAuthService", "GetAuthUsers", $"Getting auth users for domain: {domain}",
            @params: new { domain },
            tags: new[] { "nginx", "auth" });
        
        var htpasswdPath = $"/etc/nginx/.htpasswd_{domain}";
        if (!_sshService.FileExists(htpasswdPath))
        {
            _loggerMM?.Debug("NginxAuthService", "GetAuthUsers", $"Htpasswd file not found for domain: {domain}",
                @params: new { domain },
                tags: new[] { "nginx", "auth" });
            return new List<string>();
        }

        var content = _sshService.ReadFile(htpasswdPath);
        var users = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(':')[0])
            .ToList();
        
        _loggerMM?.Debug("NginxAuthService", "GetAuthUsers", $"Found {users.Count} auth users for domain: {domain}",
            @params: new { domain, userCount = users.Count },
            tags: new[] { "nginx", "auth" });
        
        return users;
    }

    private void TestAndReloadNginx()
    {
        _loggerMM?.Debug("NginxAuthService", "TestAndReloadNginx", "Testing nginx configuration",
            tags: new[] { "nginx", "reload" });
        
        try
        {
            // nginx -t outputs everything to stderr. SSH.NET captures stdout/stderr on separate channels,
            // so "2>&1" does not reliably redirect in RunCommand. Instead, rely on exit code:
            // ExecuteCommand throws if exit status != 0, so if we get past this line, the test passed.
            _sshService.ExecuteCommand("nginx -t");
            
            _loggerMM?.Debug("NginxAuthService", "TestAndReloadNginx", "Nginx configuration test successful, reloading",
                tags: new[] { "nginx", "reload" });
            
            _sshService.ExecuteCommand("systemctl reload nginx");
            
            _loggerMM?.Info("NginxAuthService", "TestAndReloadNginx", "Nginx reloaded successfully",
                tags: new[] { "nginx", "reload" });
        }
        catch (Exception ex)
        {
            _loggerMM?.Error("NginxAuthService", "TestAndReloadNginx", $"Failed to test and reload nginx: {ex.Message}",
                exception: ex,
                tags: new[] { "nginx", "reload", "error" });
            throw;
        }
    }
}
