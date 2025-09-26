using System.Net;

namespace OpenVideoFramework.RtspSource.Rtcp;

public static class RtcpSerializer
{
    public static RtcpPacket DeserializeRtcpPacket(byte[] data)
    {
        var packetType = data[1];
        var type = (RtcpPacketType)packetType;

        return type switch
        {
            RtcpPacketType.SenderReport => DeserializeSenderReport(data),
            _ => throw new ArgumentOutOfRangeException(
                nameof(packetType), packetType, $"Unsupported packet type: {packetType}")
        };
    }

    private static RtcpSenderReport DeserializeSenderReport(byte[] data)
    {
        var version = (byte)((data[0] & 0b11000000) >> 6);
        var padding = (byte)((data[0] & 0x20) >> 5);
        var receptionReportCount = (byte)(data[0] & 0x1F);
        var packetType = (RtcpPacketType)data[1];
        var length = (ushort)IPAddress.NetworkToHostOrder((short)BitConverter.ToUInt16(data, 2));

        if (length * 4 > data.Length - 4)
        {
            return null!;
        }
        
        var ssrc = (uint)IPAddress.NetworkToHostOrder((int)BitConverter.ToUInt32(data, 4));
        var senderInfo = new SenderInfo
        {
            NtpTimestampSeconds = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 8)),
            NtpTimestampFractions = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 12)),
            RtpTimestamp = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 16)),
            SendersPacketCount = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 20)),
            SendersOctetCount = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 24))
        };
        
        var reportBlocks = new ReportBlock[receptionReportCount];

        return new RtcpSenderReport
        {
            Version = version,
            Padding = padding,
            ReceptionReportCount = receptionReportCount,
            PacketType = packetType,
            SSRC = ssrc,
            SenderInfo = senderInfo,
            ReportBlocks = reportBlocks,
        };
    }

    public static byte[] SerializeReceiverReport(RtcpReceiverReport report)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream);

        // RTCP Header
        const byte version = 2;
        const byte padding = 0;
        var reportCount = (byte)report.ReportBlocks.Length;

        writer.Write((byte)((version << 6) | (padding << 5) | reportCount));
        writer.Write((byte)RtcpPacketType.ReceiverReport);

        var length = (ushort)((8 + report.ReportBlocks.Length * 24) / 4 - 1);
        writer.Write(IPAddress.HostToNetworkOrder((short)length));
        writer.Write(IPAddress.HostToNetworkOrder((int)report.SSRC));

        foreach (var block in report.ReportBlocks)
        {
            SerializeReportBlock(writer, block);
        }

        return memoryStream.ToArray();
    }

    private static void SerializeReportBlock(BinaryWriter writer, ReportBlock block)
    {
        writer.Write(IPAddress.HostToNetworkOrder((int)block.SSRC));
        writer.Write(block.FractionLost);
        
        var packetsLostNetworkOrder = IPAddress.HostToNetworkOrder(block.CumulativeNumberOfPacketsLost);
        writer.Write(BitConverter.GetBytes(packetsLostNetworkOrder)[..3]);

        writer.Write(IPAddress.HostToNetworkOrder((int)block.ExtendedHighestSequenceNumberReceived));
        writer.Write(IPAddress.HostToNetworkOrder((int)block.InterarrivalJitter));
        writer.Write(IPAddress.HostToNetworkOrder((int)block.LastSenderReportTimestamp));
        writer.Write(IPAddress.HostToNetworkOrder((int)block.DelaySinceLastSenderReport));
    }
}