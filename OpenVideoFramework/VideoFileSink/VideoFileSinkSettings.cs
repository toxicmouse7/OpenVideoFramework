namespace OpenVideoFramework.VideoFileSink;

public class VideoFileSinkSettings
{
    /// <summary>
    /// FPS for output video. If null, FPS will be estimated automatically from incoming frames.
    /// </summary>
    public double? ConstantFps { get; init; }
    
    /// <summary>
    /// Time interval after which a new video file is created.
    /// </summary>
    public TimeSpan RollPeriod { get; init; }
    
    /// <summary>
    /// Output directory and filename pattern for rolling video files.
    /// </summary>
    public required string OutputPath { get; init; }
}