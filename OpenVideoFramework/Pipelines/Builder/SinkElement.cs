using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines.Builder;

internal class SinkElement<TInput> : IPipelineElement, IDisposable
{
    private readonly IPipelineSink<TInput> _sink;
    private readonly ChannelReader<TInput> _reader;

    public SinkElement(IPipelineSink<TInput> sink, ChannelReader<TInput> reader)
    {
        _sink = sink;
        _reader = reader;
    }

    public async Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        await _sink.PrepareForExecutionAsync(context, cancellationToken);
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await _sink.ConsumeAsync(_reader, cancellationToken);
    }

    public object GetUnderlyingElement() => _sink;

    public void Dispose()
    {
        if (_sink is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}