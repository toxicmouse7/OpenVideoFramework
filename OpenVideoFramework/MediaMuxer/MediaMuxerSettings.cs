namespace OpenVideoFramework.MediaMuxer;

public enum MediaSynchronizationMode
{
    WallClock,
    SharedTimeline
}

public sealed class MediaMuxerSettings
{
    public required string OutputPath { get; init; }
    public MediaSynchronizationMode SynchronizationMode { get; init; } = MediaSynchronizationMode.WallClock;
}
