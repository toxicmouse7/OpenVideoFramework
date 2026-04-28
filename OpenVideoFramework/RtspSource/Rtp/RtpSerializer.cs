using System.Net;

namespace OpenVideoFramework.RtspSource.Rtp;

internal static class RtpSerializer
{
    public static RtpPacket DeserializeRtpPacket(byte[] data, PayloadType payloadType, uint clockRate)
    {
        var (header, payloadOffset, payloadLength) = DeserializePacket(data, payloadType);

        return new RtpPacket
        {
            Header = header,
            Content = data[payloadOffset..(payloadOffset + payloadLength)],
            ClockRate = clockRate
        };
    }

    public static RtpPacketHeader DeserializeHeader(byte[] data, PayloadType payloadType)
    {
        return DeserializePacket(data, payloadType).header;
    }

    private static (RtpPacketHeader header, int payloadOffset, int payloadLength) DeserializePacket(
        byte[] data,
        PayloadType payloadType)
    {
        if (data.Length < RtpPacketHeader.Size)
        {
            throw new RtpPacketParseException("RTP packet is shorter than the fixed header.");
        }

        var version = data[0] >> 6;
        if (version != RtpPacketHeader.Version)
        {
            throw new RtpPacketParseException($"Unsupported RTP version {version}.");
        }

        var hasPadding = (data[0] & 0x20) != 0;
        var hasExtension = (data[0] & 0x10) != 0;
        var csrcCount = data[0] & 0x0F;
        var marker = (data[1] & 0x80) != 0;
        var headerSize = RtpPacketHeader.Size + csrcCount * 4;

        if (data.Length < headerSize)
        {
            throw new RtpPacketParseException("RTP packet is shorter than the CSRC list.");
        }

        var csrc = new uint[csrcCount];
        for (var i = 0; i < csrcCount; i++)
        {
            csrc[i] = ReadUInt32NetworkOrder(data, RtpPacketHeader.Size + i * 4);
        }

        ushort headerExtensionLength = 0;
        var payloadOffset = headerSize;
        if (hasExtension)
        {
            if (data.Length < payloadOffset + 4)
            {
                throw new RtpPacketParseException("RTP packet is shorter than the extension header.");
            }

            headerExtensionLength = ReadUInt16NetworkOrder(data, payloadOffset + 2);
            payloadOffset += 4 + headerExtensionLength * 4;

            if (data.Length < payloadOffset)
            {
                throw new RtpPacketParseException("RTP packet is shorter than the declared extension data.");
            }
        }

        var payloadLength = data.Length - payloadOffset;
        if (hasPadding)
        {
            if (payloadLength == 0)
            {
                throw new RtpPacketParseException("RTP packet has padding flag set but no payload.");
            }

            var paddingLength = data[^1];
            if (paddingLength == 0 || paddingLength > payloadLength)
            {
                throw new RtpPacketParseException("RTP packet has invalid padding length.");
            }

            payloadLength -= paddingLength;
        }

        var header = new RtpPacketHeader(
            hasPadding,
            hasExtension,
            (uint)csrcCount,
            marker,
            payloadType,
            ReadUInt16NetworkOrder(data, 2),
            ReadUInt32NetworkOrder(data, 4),
            ReadUInt32NetworkOrder(data, 8),
            csrc,
            headerExtensionLength
        );

        return (header, payloadOffset, payloadLength);
    }

    private static ushort ReadUInt16NetworkOrder(byte[] data, int offset)
    {
        return (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(data, offset));
    }

    private static uint ReadUInt32NetworkOrder(byte[] data, int offset)
    {
        return (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset));
    }
}
