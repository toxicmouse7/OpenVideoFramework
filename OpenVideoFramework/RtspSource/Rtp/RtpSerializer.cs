using System.Net;

namespace OpenVideoFramework.RtspSource.Rtp;

public static class RtpSerializer
{
    public static RtpPacket DeserializeRtpPacket(byte[] data)
    {
        var header = DeserializeHeader(data);
        
        return new RtpPacket
        {
            Header = header,
            Content = data[RtpPacketHeader.Size..]
        };
    }
    
    public static RtpPacketHeader DeserializeHeader(byte[] data)
    {
        var header = new RtpPacketHeader(
            (data[0] & 0x20) != 0,
            (data[0] & 0x10) != 0,
            (byte)(data[0] & 0x0F),
            (data[1] & 0x80) != 0,
            (PayloadType)(data[1] & 0x7F),
            (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(data, 2)),
            (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 4)),
            (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 8)),
            [],
            (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(data, 14))
        );

        return header;
    }
}