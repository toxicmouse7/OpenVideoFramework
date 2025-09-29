namespace OpenVideoFramework.AudioFileSink;

public class AudioFileSinkSettings
{
    public TimeSpan RollPeriod { get; init; }
    public required string OutputPath { get; init; }
}