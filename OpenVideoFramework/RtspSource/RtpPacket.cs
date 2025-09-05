namespace OpenVideoFramework.RtspSource;

public class RtpPacket : INetworkPacket
{
    public required byte[] Data { get; init; }
    public required ProtocolType Protocol { get; init; }
    public required DateTimeOffset ReceivedTime { get; init; }
}