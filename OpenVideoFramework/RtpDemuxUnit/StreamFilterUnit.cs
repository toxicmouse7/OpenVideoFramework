using System.Threading.Channels;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.RtpDemuxUnit;

public class StreamFilterUnit : IPipelineUnit<DemuxedData, DemuxedData>
{
    private readonly Func<DemuxedData, bool> _filter;

    public StreamFilterUnit(Func<DemuxedData, bool> filter)
    {
        _filter = filter;
    }

    public Task PrepareForExecutionAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ProcessAsync(
        ChannelReader<DemuxedData> reader,
        ChannelWriter<DemuxedData> writer,
        CancellationToken cancellationToken)
    {
        await foreach (var data in reader.ReadAllAsync(cancellationToken))
        {
            if (_filter(data))
            {
                await writer.WriteAsync(data, cancellationToken);
            }
        }
    }
}