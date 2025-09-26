using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines.Builder;

internal class SourceElement<TOutput> : IPipelineElement, IDisposable
{
    private readonly IPipelineSource<TOutput> _source;
    private readonly ChannelWriter<TOutput> _writer;

    public SourceElement(IPipelineSource<TOutput> source, ChannelWriter<TOutput> writer)
    {
        _source = source;
        _writer = writer;
    }

    public async Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        await _source.PrepareForExecutionAsync(context, cancellationToken);
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

    public void Dispose()
    {
        if (_source is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}