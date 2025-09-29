namespace OpenVideoFramework.RtspSource.Rtcp;

internal class RtcpReceiverReport
{
    public required uint SSRC { get; init; }
    public required ReportBlock[] ReportBlocks { get; init; }
}