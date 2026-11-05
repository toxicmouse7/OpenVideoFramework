namespace OpenVideoFramework.Pipelines.Builder;

internal sealed class SourceElement<TOutput>(IPipelineSource<TOutput> source) : IPipelineElement
{
    public PipelineOutput<TOutput> Output { get; } = new();

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken) =>
        source.PrepareForExecutionAsync(context, cancellationToken);

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await source.ProduceAsync(Output.EmitAsync, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await Output.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public object GetUnderlyingElement() => source;
}
