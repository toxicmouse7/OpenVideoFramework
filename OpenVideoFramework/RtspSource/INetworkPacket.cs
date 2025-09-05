namespace OpenVideoFramework.RtspSource;

public enum ProtocolType
{
    Rtp,
    Rtcp,
    Unknown
}

public interface INetworkPacket
{
    byte[] Data { get; }
    ProtocolType Protocol { get; }
    DateTimeOffset ReceivedTime { get; }
}