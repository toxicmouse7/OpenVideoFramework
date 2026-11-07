namespace OpenVideoFramework.VideoFileSink;

public sealed class VideoFileSinkSettings
{
    public required string OutputPath { get; init; }
    public required TimeSpan RollPeriod { get; init; }
    public required double Fps { get; init; }
}
