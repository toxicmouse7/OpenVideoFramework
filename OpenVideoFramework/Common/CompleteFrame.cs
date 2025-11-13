namespace OpenVideoFramework.Common;

/// <summary>
/// Base frame type.
/// <seealso cref="AudioFrame"/>
/// <seealso cref="VideoFrame"/>
/// </summary>
public abstract class CompleteFrame
{
    public required byte[] Data { get; init; }
    public byte[]? ExtraData { get; init; }
    public required bool IsKeyFrame { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public required Codec Codec { get; init; }
    public required uint ClockRate { get; init; }
    public required TimeSpan Duration { get; init; }
}