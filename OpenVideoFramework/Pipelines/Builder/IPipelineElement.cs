namespace OpenVideoFramework.Pipelines.Builder;

public interface IPipelineElement
{
    Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken);
    Task ExecuteAsync(CancellationToken cancellationToken);
}