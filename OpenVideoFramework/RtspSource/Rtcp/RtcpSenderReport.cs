namespace OpenVideoFramework.RtspSource.Rtcp;

public struct SenderInfo
{
    /// <summary>
    /// NTP format time. Seconds since 1st January 1970
    /// </summary>
    public uint NtpTimestampSeconds { get; init; }
    
    /// <summary>
    /// NTP format time. Seconds fractions
    /// </summary>
    public uint NtpTimestampFractions { get; init; }
    
    /// <summary>
    /// RTP timestamp in stream units (e.g. 90000)
    /// </summary>
    public uint RtpTimestamp { get; init; }
    
    /// <summary>
    /// Total number of RTP media packets sent with the current SSRC since transmission began
    /// </summary>
    public uint SendersPacketCount { get; init; }
    
    /// <summary>
    /// Total bytes of RTP payload data (not including headers, padding, etc)
    /// sent with the current SSRC since transmission began
    /// </summary>
    public uint SendersOctetCount { get; init; }
}

internal class RtcpSenderReport : RtcpPacket
{
    public uint SSRC { get; init; }
    public SenderInfo SenderInfo { get; init; }
    public ReportBlock[] ReportBlocks { get; init; } = [];
}