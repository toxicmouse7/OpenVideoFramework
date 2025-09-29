namespace OpenVideoFramework.HttpStreamSink;

public class HttpStreamSinkSettings
{
    /// <summary>
    /// Port to serve HTTP server on.
    /// </summary>
    public int Port { get; init; } = 8080;

    /// <summary>
    /// Route for MJPEG stream.
    /// </summary>
    public required string Route { get; init; } = null!;
}