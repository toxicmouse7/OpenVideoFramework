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
    private uint _senderReportRtpTimestamp;
    private long? _extendedTimestamp;
    private long? _senderReportExtendedTimestamp;
    private DateTimeOffset? _senderReportPresentationTime;

    public uint SSRC
    {
        get;
    }
    public ulong TotalPacketsReceived
    {
        get; private set;
    }
    public uint BaseSequenceNumber
    {
        get;
    }

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
        var extended = ExtendTimestamp(packet.Header.Timestamp);
        _extendedTimestamp = Math.Max(_extendedTimestamp ?? extended, extended);
        if (_senderReportPresentationTime.HasValue && !_senderReportExtendedTimestamp.HasValue)
        {
            _senderReportExtendedTimestamp = ExtendTimestamp(_senderReportRtpTimestamp);
        }

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
        var timestampDiff = (double)unchecked((int)(packet.Header.Timestamp - _lastRtpTimestamp)) / _clockRate;

        var deviation = Math.Abs(arrivalDiff - timestampDiff);

        _jitter += (deviation - _jitter) / 16.0;

        _lastPacketArrivalTime = receivedAt;
        _lastRtpTimestamp = packet.Header.Timestamp;
    }

    public void Update(RtcpSenderReport report)
    {
        _lastSenderReportNtpTimestamp = ((ulong)report.SenderInfo.NtpTimestampSeconds << 32)
                                     | report.SenderInfo.NtpTimestampFractions;
        _lastSenderReportReceivedTime = report.ReceivedAt;
        _senderReportRtpTimestamp = report.SenderInfo.RtpTimestamp;
        _senderReportExtendedTimestamp = _extendedTimestamp.HasValue
            ? ExtendTimestamp(_senderReportRtpTimestamp)
            : null;

        // NTP seconds start in 1900, and roll over every 2^32 seconds. Reception time
        // disambiguates the era only; it is never used as presentation time.
        var epoch = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var era = (long)((report.ReceivedAt - epoch).TotalSeconds / 4294967296d);
        var seconds = era * 4294967296L + report.SenderInfo.NtpTimestampSeconds;
        var time = epoch.AddSeconds(seconds);
        if (time - report.ReceivedAt > TimeSpan.FromDays(24855))
        {
            time = time.AddSeconds(-4294967296d);
        }
        else if (report.ReceivedAt - time > TimeSpan.FromDays(24855))
        {
            time = time.AddSeconds(4294967296d);
        }

        // A zero NTP timestamp means the sender has no wall clock.
        _senderReportPresentationTime = _lastSenderReportNtpTimestamp == 0
            ? null
            : time.AddTicks((long)(((ulong)report.SenderInfo.NtpTimestampFractions * TimeSpan.TicksPerSecond) >> 32));
    }

    private long ExtendTimestamp(uint timestamp) => _extendedTimestamp is { } previous
        ? previous + unchecked((int)(timestamp - (uint)previous))
        : timestamp;

    public DateTimeOffset? GetWallClock(uint timestamp)
    {
        if (_senderReportPresentationTime is not { } time ||
            _senderReportExtendedTimestamp is not { } reference || _clockRate == 0)
        {
            return null;
        }

        var ticks = checked((long)Math.Round(
            (ExtendTimestamp(timestamp) - reference) * (decimal)TimeSpan.TicksPerSecond / _clockRate));
        return time.AddTicks(ticks);
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
