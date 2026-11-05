namespace OpenVideoFramework.Pipelines.Builder;

internal sealed class UnitElement<TInput, TOutput>(IPipelineUnit<TInput, TOutput> unit) : IPipelineElement, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public PipelineOutput<TOutput> Output { get; } = new();

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken) =>
        unit.PrepareForExecutionAsync(context, cancellationToken);

    public async ValueTask ProcessAsync(TInput input, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await unit.ProcessAsync(input, Output.EmitAsync, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await unit.CompleteAsync(Output.EmitAsync, cancellationToken).ConfigureAwait(false);
            await Output.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public object GetUnderlyingElement() => unit;
    public void Dispose() => _gate.Dispose();
}
