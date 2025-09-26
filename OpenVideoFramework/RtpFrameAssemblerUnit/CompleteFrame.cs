namespace OpenVideoFramework.RtpFrameAssemblerUnit;

public class CompleteFrame
{
    public required byte[] Data { get; init; }
    public required bool IsKeyFrame { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
}