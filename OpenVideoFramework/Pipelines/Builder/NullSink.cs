using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines.Builder;

internal class NullSink<TInput> : IPipelineSink<TInput>
{
    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ConsumeAsync(ChannelReader<TInput> input, CancellationToken cancellationToken)
    {
        await foreach (var _ in input.ReadAllAsync(cancellationToken)) ;
    }
}