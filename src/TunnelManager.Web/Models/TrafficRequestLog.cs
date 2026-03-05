namespace TunnelManager.Web.Models;

public class TrafficRequestLog
{
    public long Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string RemoteIp { get; set; } = string.Empty;
    public string RequestMethod { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
    public double? ResponseTimeMs { get; set; }
    public DateTime Timestamp { get; set; }
}
