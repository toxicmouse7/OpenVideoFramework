namespace OpenVideoFramework.Pipelines;

/// <summary>Forwards an item downstream. Shared items must be treated as read-only.</summary>
public delegate ValueTask Emit<T>(T item, CancellationToken cancellationToken);

public interface IPipelineUnit<TInput, TOutput>
{
    Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken);
    ValueTask ProcessAsync(TInput input, Emit<TOutput> emit, CancellationToken cancellationToken);
    ValueTask CompleteAsync(Emit<TOutput> emit, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
