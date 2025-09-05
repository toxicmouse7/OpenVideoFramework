using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines.Builder;

public sealed class ChannelSplitterElement<T> : IPipelineElement
{
    private readonly ChannelReader<T> _reader;
    private readonly List<ChannelWriter<T>> _branches = [];

    public ChannelSplitterElement(ChannelReader<T> reader)
    {
        _reader = reader;
    }

    public void AddBranch(ChannelWriter<T> branchWriter)
    {
        _branches.Add(branchWriter);
    }
    
    public Task PrepareForExecutionAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await foreach (var item in _reader.ReadAllAsync(cancellationToken))
        {
            foreach (var writer in _branches)
            {
                await writer.WriteAsync(item, cancellationToken);
            }
        }
        
        foreach (var writer in _branches)
        {
            writer.Complete();
        }
    }
}