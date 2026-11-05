namespace OpenVideoFramework.Pipelines;

public interface IPipelineSink<TInput>
{
    Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken);
    ValueTask ConsumeAsync(TInput input, CancellationToken cancellationToken);
    ValueTask CompleteAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
