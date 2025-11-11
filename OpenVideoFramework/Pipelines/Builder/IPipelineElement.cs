namespace OpenVideoFramework.Pipelines.Builder;

internal interface IPipelineElement
{
    Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken);
    Task ExecuteAsync(CancellationToken cancellationToken);
    object GetUnderlyingElement();
}