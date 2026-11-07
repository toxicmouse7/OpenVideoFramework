namespace OpenVideoFramework.Common;

/// <summary>One encoded video access unit. H.264 data uses Annex B.</summary>
public sealed class EncodedVideoFrame : CompleteFrame
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Codec Codec { get; init; }
    public required ReadOnlyMemory<byte> Data { get; init; }
    public required bool IsKeyFrame { get; init; }
}
