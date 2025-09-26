namespace OpenVideoFramework.RtspSource.Rtp;

public record struct RtpPacketHeader(
    bool Padding,
    bool Extension,
    uint CSRCCount,
    bool Marker,
    PayloadType PayloadType,
    ushort SequenceNumber,
    uint Timestamp,
    uint SSRC,
    uint[] CSRC,
    ushort HeaderExtensionLength)
{
    public const int Version = 2;
    public const short ExtensionsHeaderId = 0;
    
    public const int Size = 12;
}