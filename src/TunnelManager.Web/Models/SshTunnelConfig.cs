namespace TunnelManager.Web.Models;

public class SshTunnelConfig
{
    public string Name { get; set; } = string.Empty;
    public int ExternalPort { get; set; }
    public string TargetIp { get; set; } = string.Empty;
    public int TargetPort { get; set; } = 22;
    public bool IsEnabled { get; set; }
}
