using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines.Builder;

internal class SourceElement<TOutput> : IPipelineElement
{
    private readonly IPipelineSource<TOutput> _source;
    private readonly ChannelWriter<TOutput> _writer;

    public SourceElement(IPipelineSource<TOutput> source, ChannelWriter<TOutput> writer)
    {
        _source = source;
        _writer = writer;
    }

    public async Task PrepareForExecutionAsync(CancellationToken cancellationToken)
    {
        await _source.PrepareForExecutionAsync(cancellationToken);
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _source.ProduceAsync(_writer, cancellationToken);
        }
        finally
        {
            _writer.Complete();
        }
    }
}