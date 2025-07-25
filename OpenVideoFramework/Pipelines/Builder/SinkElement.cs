using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines.Builder;

internal class SinkElement<TInput> : IPipelineElement
{
    private readonly IPipelineSink<TInput> _sink;
    private readonly ChannelReader<TInput> _reader;

    public SinkElement(IPipelineSink<TInput> sink, ChannelReader<TInput> reader)
    {
        _sink = sink;
        _reader = reader;
    }

    public async Task PrepareForExecutionAsync(CancellationToken cancellationToken)
    {
        await _sink.PrepareForExecutionAsync(cancellationToken);
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await _sink.ConsumeAsync(_reader, cancellationToken);
    }
}