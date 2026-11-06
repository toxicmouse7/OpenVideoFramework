namespace OpenVideoFramework.Pipelines.Builder;

internal sealed class ChannelSplitterElement<T> : IPipelineElement, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public bool IsBranch => true;
    public PipelineOutput<T> Continuation { get; } = new();
    public PipelineOutput<T> Branch { get; } = new();

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async ValueTask ProcessAsync(T item, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Branch.EmitAsync(item, cancellationToken).ConfigureAwait(false);
            await Continuation.EmitAsync(item, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        await Branch.CompleteAsync(cancellationToken).ConfigureAwait(false);
        await Continuation.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public object? GetUnderlyingElement() => null;
    public void Dispose() => _gate.Dispose();
}
