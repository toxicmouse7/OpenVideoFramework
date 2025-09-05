namespace OpenVideoFramework.RtpDemuxUnit;

public class StreamTracker
{
    private readonly Dictionary<uint, MediaStreamContext> _streams = [];
    private readonly MediaTypeDetector _detector = new();

    public MediaStreamContext GetOrCreateStream(uint ssrc, byte payloadType)
    {
        if (_streams.TryGetValue(ssrc, out var context)) return context;

        var (mediaType, codec) = _detector.DetectFromPayloadType(payloadType);
        context = new MediaStreamContext(ssrc, payloadType, mediaType, codec);
        _streams[ssrc] = context;

        return context;
    }
}