using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines;

public interface IPipelineSink<TInput>
{
    Task PrepareForExecutionAsync(CancellationToken cancellationToken);
    Task ConsumeAsync(ChannelReader<TInput> input, CancellationToken cancellationToken);
}