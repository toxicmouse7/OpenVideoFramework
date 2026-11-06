namespace OpenVideoFramework.Pipelines.Builder;

internal sealed class NullSink<TInput>(bool disposeItems) : IPipelineSink<TInput>
{
    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async ValueTask ConsumeAsync(TInput input, CancellationToken cancellationToken)
    {
        // An item can still be in use by a buffered branch; the producer owns shared items.
        if (!disposeItems)
        {
            return;
        }

        if (input is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (input is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
