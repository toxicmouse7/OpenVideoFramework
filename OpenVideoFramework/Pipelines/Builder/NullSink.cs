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
        await foreach (var data in input.ReadAllAsync(cancellationToken))
        {
            switch (data)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }
}