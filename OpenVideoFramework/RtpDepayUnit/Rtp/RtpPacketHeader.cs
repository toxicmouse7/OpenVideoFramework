namespace OpenVideoFramework.RtpDepayUnit.Rtp;

public record struct RtpPacketHeader(
    bool Padding,
    bool Extension,
    uint CSRCCount,
    bool Marker,
    byte PayloadType,
    ushort SequenceNumber,
    uint Timestamp,
    uint SSRCIdentifier,
    uint[] CSRC,
    ushort HeaderExtensionLength)
{
    public const int Version = 2;
    public const short ExtensionsHeaderId = 0;

    public int Size => 12;
    
    public static RtpPacketHeader Deserialize(byte[] data)
    {
        var header = new RtpPacketHeader(
            (data[0] & 0x20) != 0,
            (data[0] & 0x10) != 0,
            (byte)(data[0] & 0x0F),
            (data[1] & 0x80) != 0,
            (byte)(data[1] & 0x7F),
            BitConverter.ToUInt16(data, 2),
            BitConverter.ToUInt32(data, 4),
            BitConverter.ToUInt32(data, 8),
            [],
            BitConverter.ToUInt16(data, 14)
        );

        return header;
    }
}