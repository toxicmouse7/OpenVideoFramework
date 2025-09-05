using System.Threading.Channels;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtpDepayUnit.Rtp;

namespace OpenVideoFramework.RtpDemuxUnit;

public class RtpDemuxUnit : IPipelineUnit<RtpPacket, DemuxedData>
{
    private readonly StreamTracker _streamTracker = new();
    private readonly SequenceProcessor _sequenceProcessor = new();
    
    public Task PrepareForExecutionAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ProcessAsync(
        ChannelReader<RtpPacket> reader,
        ChannelWriter<DemuxedData> writer,
        CancellationToken cancellationToken)
    {
        await foreach (var rtpPacket in reader.ReadAllAsync(cancellationToken))
        {
            var demuxedData = ProcessPacket(rtpPacket);
            await writer.WriteAsync(demuxedData, cancellationToken);
        }
    }

    private DemuxedData ProcessPacket(RtpPacket packet)
    {
        var streamContext = _streamTracker.GetOrCreateStream(packet.Header.SSRCIdentifier, packet.Header.PayloadType);
        
        _sequenceProcessor.ProcessSequence(streamContext, packet.Header.SequenceNumber);

        return new DemuxedData
        {
            Ssrc = packet.Header.SSRCIdentifier,
            PayloadType = packet.Header.PayloadType,
            Timestamp = packet.Header.Timestamp,
            SequenceNumber = packet.Header.SequenceNumber,
            Payload = packet.Content,
            IsMarker = packet.Header.Marker,
            StreamContext = streamContext
        };
    }
}