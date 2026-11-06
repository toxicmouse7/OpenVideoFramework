namespace OpenVideoFramework.Pipelines.Builder;

internal sealed class SinkElement<TInput>(IPipelineSink<TInput> sink) : IPipelineElement, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken) =>
        sink.PrepareForExecutionAsync(context, cancellationToken);

    public async ValueTask ProcessAsync(TInput input, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await sink.ConsumeAsync(input, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await sink.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public object GetUnderlyingElement() => sink;
    public void Dispose() => _gate.Dispose();
}
