using OpenVideoFramework.RtspSource.Rtcp;

namespace OpenVideoFramework.RtspSource;

internal class StreamSyncContext
{
    public uint SSRC { get; }
    public DateTimeOffset LastNtpTime { get; private set; }
    public uint LastRtpTimestamp { get; private set; }
    public bool IsSynchronized { get; private set; }

    public StreamSyncContext(uint ssrc)
    {
        SSRC = ssrc;
    }
    
    public void UpdateFromSenderReport(RtcpSenderReport senderReport)
    {
        LastNtpTime = NtpTimestampConverter.ConvertToDateTime(
            senderReport.SenderInfo.NtpTimestampSeconds,
            senderReport.SenderInfo.NtpTimestampFractions);
        LastRtpTimestamp = senderReport.SenderInfo.RtpTimestamp;
        IsSynchronized = true;
    }
    
    public DateTimeOffset? ConvertToUtc(uint rtpTimestamp, uint clockRate)
    {
        if (!IsSynchronized) return null;
        
        var rtpDelta = rtpTimestamp - LastRtpTimestamp;
        
        var timeDeltaSeconds = (double)rtpDelta / clockRate;
        
        return LastNtpTime.AddSeconds(timeDeltaSeconds);
    }
}