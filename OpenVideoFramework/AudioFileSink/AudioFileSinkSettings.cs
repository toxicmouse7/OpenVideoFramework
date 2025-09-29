namespace OpenVideoFramework.AudioFileSink;

public class AudioFileSinkSettings
{
    /// <summary>
    /// Time interval after which a new video file is created.
    /// </summary>
    public required TimeSpan RollPeriod { get; init; }
    
    /// <summary>
    /// Output directory and filename pattern for rolling video files.
    /// E.g. /some/path/audio.ac3
    /// </summary>
    public required string OutputPath { get; init; }
}