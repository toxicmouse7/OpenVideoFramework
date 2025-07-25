namespace OpenVideoFramework.RtspSource;

public class RtspResponse
{
    public int StatusCode { get; set; }
    public Dictionary<string, string> Headers { get; } = new();
    public string Body { get; set; } = string.Empty;
}
