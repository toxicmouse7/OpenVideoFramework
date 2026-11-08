namespace OpenVideoFramework.RtspSource;

internal class RtspResponse
{
    public int StatusCode
    {
        get; set;
    }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; set; } = string.Empty;
}
