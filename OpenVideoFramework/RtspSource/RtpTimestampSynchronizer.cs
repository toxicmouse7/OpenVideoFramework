using System.Collections.Concurrent;
using OpenVideoFramework.RtspSource.Rtcp;

namespace OpenVideoFramework.RtspSource;

public class RtpTimestampSynchronizer
{
    private readonly ConcurrentDictionary<uint, StreamSyncContext> _streamContexts = new();
    
    public void UpdateFromSenderReport(RtcpSenderReport sr)
    {
        var context = _streamContexts.GetOrAdd(sr.SSRC, _ => new StreamSyncContext(sr.SSRC));
        context.UpdateFromSenderReport(sr);
    }
    
    public DateTimeOffset? ConvertRtpTimestampToUtc(uint ssrc, uint rtpTimestamp, uint clockRate)
    {
        return _streamContexts.TryGetValue(ssrc, out var context) ? context.ConvertToUtc(rtpTimestamp, clockRate) : null;
    }
}