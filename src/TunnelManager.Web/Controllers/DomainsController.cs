using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TunnelManager.Web.Services;

namespace TunnelManager.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DomainsController : ControllerBase
{
    private readonly NginxService _nginxService;
    private readonly FolderService _folderService;
    private readonly HealthCheckService _healthCheckService;
    private readonly IConfiguration _configuration;

    public DomainsController(
        NginxService nginxService,
        FolderService folderService,
        HealthCheckService healthCheckService,
        IConfiguration configuration)
    {
        _nginxService = nginxService;
        _folderService = folderService;
        _healthCheckService = healthCheckService;
        _configuration = configuration;
    }

    [HttpGet("folders")]
    [AllowAnonymous]
    public IActionResult GetDomainFolders()
    {
        if (!IsAuthorized())
        {
            Response.Headers["WWW-Authenticate"] = "Basic realm=\"folders\"";
            return Unauthorized();
        }

        var forwards = _nginxService.GetAllForwards();
        var folderMap = _folderService.GetDomainFolders();
        var healthMap = _healthCheckService.GetAllStatuses();

        var result = forwards
            .Select(f =>
            {
                healthMap.TryGetValue(f.Domain, out var health);
                return new
                {
                    domain = f.Domain,
                    folder = folderMap.TryGetValue(f.Domain, out var folder) ? folder : string.Empty,
                    target = f.Target,
                    hasSsl = f.HasSsl,
                    hasAuth = f.HasAuth,
                    hasWebSocket = f.HasWebSocket,
                    isEnabled = f.IsEnabled,
                    dnsOk = health?.DnsOk,
                    localOk = health?.LocalOk,
                    lastChecked = health?.LastChecked
                };
            })
            .ToList();

        return Ok(result);
    }

    private bool IsAuthorized()
    {
        // Existing app session (cookie auth) is allowed.
        if (User.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        // Endpoint-only Basic-auth credentials configured separately from the app login.
        var expectedUser = _configuration["ApiAccess:Folders:Username"];
        var expectedPass = _configuration["ApiAccess:Folders:Password"];
        if (string.IsNullOrEmpty(expectedUser) || string.IsNullOrEmpty(expectedPass))
        {
            return false;
        }

        if (!Request.Headers.TryGetValue("Authorization", out var header))
        {
            return false;
        }

        var value = header.ToString();
        const string prefix = "Basic ";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value[prefix.Length..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }

        var sep = decoded.IndexOf(':');
        if (sep < 0)
        {
            return false;
        }

        var user = decoded[..sep];
        var pass = decoded[(sep + 1)..];

        return FixedTimeEquals(user, expectedUser) & FixedTimeEquals(pass, expectedPass);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ab.Length != bb.Length)
        {
            CryptographicOperations.FixedTimeEquals(ab, ab);
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}
