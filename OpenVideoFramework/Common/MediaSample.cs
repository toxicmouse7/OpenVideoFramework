namespace OpenVideoFramework.Common;

/// <summary>
/// A media frame and its timing. Relative timestamps share a timeline only when the source establishes one;
/// independently based RTP tracks can be synchronized using their reliable WallClock values.
/// WallClock is absolute presentation time, not necessarily physical capture time.
/// </summary>
public sealed record MediaSample<T>(
    T Frame,
    int TrackId,
    TimeSpan? Pts = null,
    TimeSpan? Dts = null,
    DateTimeOffset? WallClock = null) where T : CompleteFrame;
