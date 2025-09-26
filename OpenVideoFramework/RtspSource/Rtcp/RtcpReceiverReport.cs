namespace OpenVideoFramework.RtspSource.Rtcp;

public class RtcpReceiverReport
{
    public required uint SSRC { get; init; }
    public required ReportBlock[] ReportBlocks { get; init; }
}