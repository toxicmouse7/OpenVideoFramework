namespace OpenVideoFramework.RtpDemuxUnit;

public class MediaTypeDetector
{
    public (MediaType mediaType, Codec codec) DetectFromPayloadType(byte payloadType)
    {
        return payloadType switch
        {
            26 => (MediaType.Video, Codec.MJPEG),
            96 => (MediaType.Video, Codec.H264),
            97 => (MediaType.Video, Codec.H265),
            98 => (MediaType.Video, Codec.MPEG4),
            99 => (MediaType.Audio, Codec.AAC),
            100 => (MediaType.Audio, Codec.PCM),
            101 => (MediaType.Audio, Codec.G711),
            _ => (MediaType.Unknown, Codec.Unknown)
        };
    }
}