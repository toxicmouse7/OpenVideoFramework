namespace OpenVideoFramework.Pipelines;

public interface IPipelineSource<TOutput>
{
    Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken);
    Task ProduceAsync(Emit<TOutput> emit, CancellationToken cancellationToken);
}
