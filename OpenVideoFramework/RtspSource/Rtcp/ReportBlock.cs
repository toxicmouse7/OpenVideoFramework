namespace OpenVideoFramework.RtspSource.Rtcp;

public struct ReportBlock
{
    public uint SSRC { get; init; }
    
    /// <summary>
    /// Fraction of packets lost since the last ReportBlock about the stream was sent,
    /// calculated based on the sequence number of packets received.
    /// For example, 25 fraction lost is equal to ~10% (25 / 256 ~= 0.098)
    /// </summary>
    public byte FractionLost { get; init; }
    
    /// <summary>
    /// Total number of packets lost since the stream began (not since last ReportBlock).
    /// </summary>
    public int CumulativeNumberOfPacketsLost { get; init; }
    
    /// <summary>
    /// Highest sequence number of a packet received
    /// </summary>
    public uint ExtendedHighestSequenceNumberReceived { get; init; }
    
    public uint InterarrivalJitter { get; init; }
    
    /// <summary>
    /// Middle 32 bits of the most recent NTP timestamp
    /// from the <see cref="SenderInfo"/> associated with this RTP stream
    /// </summary>
    public uint LastSenderReportTimestamp { get; init; }
    
    /// <summary>
    /// Time, in units of 1/65536ths of a second, between receiving the last SenderInfo
    /// associated with this RTP stream and sending this ReportBlock
    /// </summary>
    public uint DelaySinceLastSenderReport { get; init; }
}