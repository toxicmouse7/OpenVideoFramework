using OpenVideoFramework.Common;

namespace OpenVideoFramework.RtspSource;

public class RtspSourceConfiguration
{
    public required string Url { get; init; } = null!;
    public MediaType? AllowedMediaType { get; init; } = null!;
}