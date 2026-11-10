using RtpPacket = OpenVideoFramework.RtspSource.Rtp.RtpPacket;

namespace OpenVideoFramework.RtspSource.Rtcp;

internal class RtcpStatisticsService
{
    private readonly Dictionary<uint, RtcpSenderReport> _pendingSenderReports = [];
    private readonly Dictionary<uint, StreamStatistics> _streamStatistics = [];
    private readonly uint _ssrc = (uint)Random.Shared.Next();

    public void UpdateStatistics(RtpPacket rtpPacket, uint clockRate, DateTimeOffset receivedAt)
    {
        if (clockRate == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clockRate), "RTP clock rate must be positive.");
        }

        if (!_streamStatistics.TryGetValue(rtpPacket.Header.SSRC, out var streamStatistics))
        {
            streamStatistics = new StreamStatistics(
                rtpPacket.Header.SSRC,
                rtpPacket.Header.SequenceNumber,
                clockRate);
            _streamStatistics[rtpPacket.Header.SSRC] = streamStatistics;
        }

        if (_pendingSenderReports.Remove(rtpPacket.Header.SSRC, out var senderReport))
        {
            streamStatistics.Update(senderReport);
        }

        streamStatistics.Update(rtpPacket, receivedAt);
    }

    public void UpdateStatistics(RtcpSenderReport senderReport)
    {
        var statistics = _streamStatistics.GetValueOrDefault(senderReport.SSRC);

        if (statistics is null)
        {
            _pendingSenderReports[senderReport.SSRC] = senderReport;
        }
        else
        {
            statistics.Update(senderReport);
        }
    }

    public RtcpReceiverReport? GetReceiverReport(uint ssrc)
    {
        var statistics = _streamStatistics.GetValueOrDefault(ssrc);
        if (statistics is null)
        {
            return null;
        }

        var receiverReport = new RtcpReceiverReport
        {
            SSRC = _ssrc,
            ReportBlocks = [statistics.GenerateReportBlock()]
        };

        return receiverReport;
    }

    public DateTimeOffset? GetWallClock(uint ssrc, uint timestamp) =>
        _streamStatistics.GetValueOrDefault(ssrc)?.GetWallClock(timestamp);
}
