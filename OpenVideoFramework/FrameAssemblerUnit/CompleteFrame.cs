using OpenVideoFramework.RtpDemuxUnit;

namespace OpenVideoFramework.FrameAssemblerUnit;

public class CompleteFrame
{
    public required byte[] Data { get; init; }
    public required bool IsKeyFrame { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}