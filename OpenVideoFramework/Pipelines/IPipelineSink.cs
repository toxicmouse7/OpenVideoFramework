using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines;

public interface IPipelineSink<TInput>
{
    Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken);
    Task ConsumeAsync(ChannelReader<TInput> input, CancellationToken cancellationToken);
}