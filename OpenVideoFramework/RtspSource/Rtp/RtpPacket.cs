namespace OpenVideoFramework.RtspSource.Rtp;

public class RtpPacket
{
    public RtpPacketHeader Header
    {
        get; init;
    }
    public byte[] Content { get; init; } = null!;
    public DateTimeOffset ReceivedAt { get; } = DateTimeOffset.Now;
}
