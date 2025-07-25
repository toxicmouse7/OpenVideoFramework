using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines;

public interface IPipelineUnit<TInput, TOutput>
{
    Task PrepareForExecutionAsync(CancellationToken cancellationToken);
    Task ProcessAsync(ChannelReader<TInput> reader, ChannelWriter<TOutput> writer, CancellationToken cancellationToken);
}