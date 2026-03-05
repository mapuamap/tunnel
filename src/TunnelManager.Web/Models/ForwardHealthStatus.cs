namespace TunnelManager.Web.Models;

public class ForwardHealthStatus
{
    public string Domain { get; set; } = string.Empty;
    public bool? DnsOk { get; set; }       // null = not checked yet
    public bool? LocalOk { get; set; }      // null = not checked yet
    public DateTime? LastChecked { get; set; }
}
