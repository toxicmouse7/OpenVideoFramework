namespace OpenVideoFramework.Pipelines.Builder;

internal interface IPipelineElement
{
    bool IsBranch => false;
    Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken);
    object? GetUnderlyingElement();
}

internal sealed class PipelineOutput<T>
{
    private Emit<T>? _emit;
    private Func<CancellationToken, Task>? _complete;

    public void Connect(Emit<T> emit, Func<CancellationToken, Task> complete)
    {
        if (_emit is not null)
        {
            throw new InvalidOperationException("This pipeline output is already connected.");
        }

        _emit = emit;
        _complete = complete;
    }

    public ValueTask EmitAsync(T item, CancellationToken cancellationToken) =>
        (_emit ?? throw new InvalidOperationException("Unconnected pipeline output."))(item, cancellationToken);

    public Task CompleteAsync(CancellationToken cancellationToken) =>
        (_complete ?? throw new InvalidOperationException("Unconnected pipeline output."))(cancellationToken);
}
