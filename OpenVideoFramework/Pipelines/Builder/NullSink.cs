using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines.Builder;

internal class NullSink<TInput> : IPipelineSink<TInput>
{
    public Task PrepareForExecutionAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ConsumeAsync(ChannelReader<TInput> input, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await input.ReadAsync(cancellationToken);
        }
    }
}