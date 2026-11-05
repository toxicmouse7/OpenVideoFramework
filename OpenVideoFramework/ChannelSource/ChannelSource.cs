using System.Threading.Channels;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.ChannelSource;

public class ChannelSource<TSource> : IPipelineSource<TSource>
{
    private readonly Channel<TSource> _source;

    public ChannelSource(Channel<TSource> source)
    {
        _source = source;
    }
    
    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ProduceAsync(Emit<TSource> emit, CancellationToken cancellationToken)
    {
        await foreach (var item in _source.Reader.ReadAllAsync(cancellationToken))
        {
            await emit(item, cancellationToken);
        }
    }
}