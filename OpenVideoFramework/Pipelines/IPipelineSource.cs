using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines;

public interface IPipelineSource<TOutput>
{
    Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken);
    Task ProduceAsync(ChannelWriter<TOutput> output, CancellationToken cancellationToken);
}