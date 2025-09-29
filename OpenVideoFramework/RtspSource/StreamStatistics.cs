using OpenVideoFramework.RtspSource.Rtcp;
using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtspSource;

internal class StreamStatistics
{
    // Highest sequence number related
    private ushort _maxSequenceNumber;
    private uint _cycles;

    // Fraction lost related
    private ulong _expectedPrior;
    private ulong _receivedPrior;

    // Jitter related
    private readonly uint _clockRate;
    private DateTimeOffset _lastPacketArrivalTime;
    private uint _lastRtpTimestamp;
    private double _jitter;
    
    // Sender report info
    private ulong _lastSenderReportNtpTimestamp;
    private DateTimeOffset _lastSenderReportReceivedTime;

    public uint SSRC { get; }
    public ulong TotalPacketsReceived { get; private set; }
    public uint BaseSequenceNumber { get; }

    public uint ExtendedHighestSequenceNumberReceived =>
        (_cycles << 16) + _maxSequenceNumber;

    public StreamStatistics(uint ssrc, uint baseSequenceNumber, uint clockRate)
    {
        SSRC = ssrc;
        BaseSequenceNumber = baseSequenceNumber;
        _clockRate = clockRate;
        _expectedPrior = 0;
        _receivedPrior = 0;
        _jitter = 0;
    }

    public void Update(RtpPacket packet, DateTimeOffset receivedAt)
    {
        TotalPacketsReceived++;

        var seq = packet.Header.SequenceNumber;
        
        if ((ushort)(seq - _maxSequenceNumber) < 0x8000)
        {
            if (seq > _maxSequenceNumber)
            {
                _maxSequenceNumber = seq;
            }
        }
        else
        {
            if (seq < _maxSequenceNumber)
            {
                _cycles++;
            }

            _maxSequenceNumber = seq;
        }

        // Jitter
        if (TotalPacketsReceived == 1)
        {
            _lastPacketArrivalTime = receivedAt;
            _lastRtpTimestamp = packet.Header.Timestamp;
            return;
        }

        var arrivalDiff = (receivedAt - _lastPacketArrivalTime).TotalSeconds;
        var timestampDiff = (double)(packet.Header.Timestamp - _lastRtpTimestamp) / _clockRate;

        var deviation  = Math.Abs(arrivalDiff - timestampDiff);

        _jitter += (deviation - _jitter) / 16.0;
        
        _lastPacketArrivalTime = receivedAt;
        _lastRtpTimestamp = packet.Header.Timestamp;
    }

    public void Update(RtcpSenderReport report)
    {
        _lastSenderReportNtpTimestamp = ((ulong)report.SenderInfo.NtpTimestampSeconds << 32)
                                     | report.SenderInfo.NtpTimestampFractions;
        _lastSenderReportReceivedTime = report.ReceivedAt;
    }

    public ReportBlock GenerateReportBlock()
    {
        // Cumulative loss
        var extendedMax = ExtendedHighestSequenceNumberReceived;
        var expected = extendedMax - BaseSequenceNumber + 1;
        var lost = (long)(expected - TotalPacketsReceived);
        var clampedLost = (int)Math.Clamp(lost, -0x800000, 0x7FFFFF);

        // Fraction lost
        var expectedInterval = expected - _expectedPrior;
        var receivedInterval = TotalPacketsReceived - _receivedPrior;
        var lostInterval = (long)(expectedInterval - receivedInterval);

        byte fractionLost = 0;
        if (expectedInterval > 0 && lostInterval > 0)
        {
            fractionLost = (byte)((ulong)(lostInterval << 8) / expectedInterval);
        }

        // Save values for next report
        _expectedPrior = expected;
        _receivedPrior = TotalPacketsReceived;

        var jitterReportValue = (uint)Math.Round(_jitter * _clockRate);
        
        // Middle bytes of sender report NTP timestamp
        var ntpFraction = (uint)(_lastSenderReportNtpTimestamp & 0xFFFFFFFF);
        var ntpSeconds = (uint)(_lastSenderReportNtpTimestamp >> 32);
        var lsrTimestamp = (ntpSeconds << 16) | (ntpFraction >> 16);
        
        // Delay since last sender report
        uint dlsr = 0;
        if (_lastSenderReportReceivedTime != DateTimeOffset.MinValue)
        {
            var delaySinceLastSenderReport = DateTimeOffset.Now - _lastSenderReportReceivedTime;
            dlsr = (uint)Math.Round(delaySinceLastSenderReport.TotalSeconds * 65536);
        }
        
        return new ReportBlock
        {
            SSRC = SSRC,
            CumulativeNumberOfPacketsLost = clampedLost,
            ExtendedHighestSequenceNumberReceived = (uint)extendedMax,
            FractionLost = fractionLost,
            InterarrivalJitter = jitterReportValue,
            DelaySinceLastSenderReport = dlsr,
            LastSenderReportTimestamp = lsrTimestamp
        };
    }
}