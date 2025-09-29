using System.Net;

namespace OpenVideoFramework.RtspSource.Rtp;

internal static class RtpSerializer
{
    public static RtpPacket DeserializeRtpPacket(byte[] data, PayloadType payloadType, uint clockRate)
    {
        var header = DeserializeHeader(data, payloadType);
        
        return new RtpPacket
        {
            Header = header,
            Content = data[RtpPacketHeader.Size..],
            ClockRate = clockRate
        };
    }
    
    public static RtpPacketHeader DeserializeHeader(byte[] data, PayloadType payloadType)
    {
        var header = new RtpPacketHeader(
            (data[0] & 0x20) != 0,
            (data[0] & 0x10) != 0,
            (byte)(data[0] & 0x0F),
            (data[1] & 0x80) != 0,
            payloadType,
            (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(data, 2)),
            (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 4)),
            (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 8)),
            [],
            (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(data, 14))
        );

        return header;
    }
}