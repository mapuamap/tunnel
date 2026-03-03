using System.Text.RegularExpressions;

namespace TunnelManager.Web.Services;

public class WireGuardService
{
    private readonly SshService _sshService;
    private readonly ILogger<WireGuardService> _logger;

    public WireGuardService(SshService sshService, ILogger<WireGuardService> logger)
    {
        _sshService = sshService;
        _logger = logger;
    }

    public WireGuardStatus GetStatus()
    {
        try
        {
            var result = _sshService.ExecuteCommand("wg show 2>&1");
            return ParseStatus(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get WireGuard status");
            return new WireGuardStatus { IsRunning = false };
        }
    }

    private WireGuardStatus ParseStatus(string output)
    {
        var status = new WireGuardStatus { IsRunning = output.Contains("interface:") };

        if (!status.IsRunning)
        {
            return status;
        }

        // Parse interface
        var interfaceMatch = Regex.Match(output, @"interface:\s+(\w+)");
        if (interfaceMatch.Success)
        {
            status.InterfaceName = interfaceMatch.Groups[1].Value;
        }

        // Parse public key
        var publicKeyMatch = Regex.Match(output, @"public key:\s+([A-Za-z0-9+/=]+)");
        if (publicKeyMatch.Success)
        {
            status.PublicKey = publicKeyMatch.Groups[1].Value;
        }

        // Parse peers
        var peerMatches = Regex.Matches(output, @"peer:\s+([A-Za-z0-9+/=]+)");
        status.PeerCount = peerMatches.Count;

        // Parse transfer
        var transferMatch = Regex.Match(output, @"transfer:\s+(\d+)\s+received,\s+(\d+)\s+sent");
        if (transferMatch.Success)
        {
            status.BytesReceived = long.Parse(transferMatch.Groups[1].Value);
            status.BytesSent = long.Parse(transferMatch.Groups[2].Value);
        }

        // Parse latest handshake
        var handshakeMatch = Regex.Match(output, @"latest handshake:\s+([^\n]+)");
        if (handshakeMatch.Success)
        {
            status.LatestHandshake = handshakeMatch.Groups[1].Value.Trim();
        }

        return status;
    }
}

public class WireGuardStatus
{
    public bool IsRunning { get; set; }
    public string InterfaceName { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public int PeerCount { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public string LatestHandshake { get; set; } = string.Empty;
}
