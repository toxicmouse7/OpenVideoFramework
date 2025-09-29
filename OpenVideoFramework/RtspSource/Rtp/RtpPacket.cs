namespace OpenVideoFramework.RtspSource.Rtp;

public class RtpPacket
{
    public RtpPacketHeader Header { get; init; }
    public byte[] Content { get; init; } = null!;
    public uint ClockRate { get; init; }
    public DateTimeOffset ReceivedAt { get; } = DateTimeOffset.Now;
    public DateTimeOffset? Timestamp { get; private set; }

    public void Stamp(DateTimeOffset timestamp)
    {
        Timestamp = timestamp;
    }
}