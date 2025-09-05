namespace OpenVideoFramework.RtpDemuxUnit;

public class MediaStreamContext
{
    public MediaStreamContext(uint ssrc, byte payloadType, MediaType mediaType, Codec codec)
    {
        Ssrc = ssrc;
        PayloadType = payloadType;
        MediaType = mediaType;
        Codec = codec;
    }
    
    public uint Ssrc { get; }
    public byte PayloadType { get; }
    public MediaType MediaType { get; }
    public Codec Codec { get; }
    public DateTime FirstPacketTime { get; }
    public DateTimeOffset LastPacketTime { get; set; }
    public long PacketCount { get; set; }
    public long LostPacketCount { get; set; }
    public ushort LastSequenceNumber { get; set; }
}