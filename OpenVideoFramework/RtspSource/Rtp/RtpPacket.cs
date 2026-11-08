namespace OpenVideoFramework.RtspSource.Rtp;

public class RtpPacket
{
    public RtpPacketHeader Header
    {
        get; init;
    }
    public byte[] Content { get; init; } = null!;
    internal IReadOnlyList<byte[]>? H264ParameterSets { get; init; }
    public DateTimeOffset ReceivedAt { get; } = DateTimeOffset.Now;
}
