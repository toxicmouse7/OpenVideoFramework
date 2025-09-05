namespace OpenVideoFramework.RtpDepayUnit.Rtp;

public class RtpPacket
{
    public RtpPacketHeader Header { get; init; }
    public byte[] Content { get; init; } = null!;
}