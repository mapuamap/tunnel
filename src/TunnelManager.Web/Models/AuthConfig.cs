namespace TunnelManager.Web.Models;

public class AuthConfig
{
    public string Domain { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Realm { get; set; } = "Restricted Access";
    public bool IsEnabled { get; set; }
}
