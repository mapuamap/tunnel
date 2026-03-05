namespace TunnelManager.Web.Models;

public class TrafficStatsHourly
{
    public long Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public long Requests { get; set; }
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public int UniqueIps { get; set; }
    public int Status2xx { get; set; }
    public int Status3xx { get; set; }
    public int Status4xx { get; set; }
    public int Status5xx { get; set; }
    public double? AvgResponseTimeMs { get; set; }
}
