namespace TunnelManager.Web.Models;

public class ForwardConfig
{
    public string Domain { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty; // Format: IP:PORT
    public bool HasSsl { get; set; }
    public bool HasAuth { get; set; }
    public bool HasWebSocket { get; set; }
    public bool IsEnabled { get; set; }
}
