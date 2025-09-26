using RtpPacket = OpenVideoFramework.RtspSource.Rtp.RtpPacket;

namespace OpenVideoFramework.RtspSource.Rtcp;

public class RtcpStatisticsService
{
    private readonly Queue<RtcpSenderReport> _srQueue = [];
    private readonly Dictionary<uint, StreamStatistics> _streamStatistics = new();
    private readonly uint _ssrc = (uint)Random.Shared.Next();

    public void UpdateStatistics(RtpPacket rtpPacket, uint clockRate, DateTimeOffset receivedAt)
    {
        if (!_streamStatistics.TryGetValue(rtpPacket.Header.SSRC, out var streamStatistics))
        {
            streamStatistics = new StreamStatistics(rtpPacket.Header.SSRC,
                rtpPacket.Header.SequenceNumber,
                clockRate);
            _streamStatistics[rtpPacket.Header.SSRC] = streamStatistics;
        }

        while (_srQueue.Count != 0)
        {
            var senderReport = _srQueue.Dequeue();
            streamStatistics.Update(senderReport);
        }

        streamStatistics.Update(rtpPacket, receivedAt);
    }

    public void UpdateStatistics(RtcpSenderReport senderReport)
    {
        var statistics = _streamStatistics.GetValueOrDefault(senderReport.SSRC);

        if (statistics is null)
        {
            _srQueue.Enqueue(senderReport);
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
}