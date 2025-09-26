namespace OpenVideoFramework.RtspSource.SDP;

public class SdpRegistry
{
    private readonly Dictionary<byte, TrackMetadata> _metadataByPayloadType = new();
    
    public void RegisterSdp(IEnumerable<TrackMetadata> sdp)
    {
        foreach (var streamMetadata in sdp)
        {
            _metadataByPayloadType[streamMetadata.PayloadType] = streamMetadata;
        }
    }
    
    public TrackMetadata GetByPayloadType(byte payloadType)
    {
        return _metadataByPayloadType[payloadType];
    }

    public IReadOnlyCollection<TrackMetadata> GetAll()
    {
        return _metadataByPayloadType.Values;
    }
}