using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines;

public interface IPipelineSource<TOutput>
{
    Task PrepareForExecutionAsync(CancellationToken cancellationToken);
    Task ProduceAsync(ChannelWriter<TOutput> output, CancellationToken cancellationToken);
}