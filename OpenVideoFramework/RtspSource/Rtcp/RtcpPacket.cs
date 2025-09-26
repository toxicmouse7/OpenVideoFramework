namespace OpenVideoFramework.RtspSource.Rtcp;

public abstract class RtcpPacket
{
    public byte Version { get; init; }
    public byte Padding { get; init; }
    public byte ReceptionReportCount { get; init; }
    public RtcpPacketType PacketType { get; init; }
    public ushort Length { get; init; }
    public DateTimeOffset ReceivedAt { get; } = DateTimeOffset.Now;
}