namespace TunnelManager.Web.Models;

public class DomainDailySummary
{
    public long Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public long TotalRequests { get; set; }
    public long TotalBytesSent { get; set; }
    public long TotalBytesReceived { get; set; }
    public int UniqueIps { get; set; }
    public int Status2xx { get; set; }
    public int Status3xx { get; set; }
    public int Status4xx { get; set; }
    public int Status5xx { get; set; }
    public double? AvgResponseTimeMs { get; set; }
}
