using System.Threading.Channels;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.FrameFilterUnit;

/// <summary>
/// Filters <see cref="CompleteFrame"/> based on generic type.
/// </summary>
/// <typeparam name="TFrameType">>Type to filter on. Must be a derived type from <see cref="CompleteFrame"/></typeparam>
public class FrameFilterUnit<TFrameType> : IPipelineUnit<CompleteFrame, TFrameType>
    where TFrameType : CompleteFrame
{
    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ProcessAsync(
        ChannelReader<CompleteFrame> reader,
        ChannelWriter<TFrameType> writer,
        CancellationToken cancellationToken)
    {
        await foreach (var frame in reader.ReadAllAsync(cancellationToken))
        {
            if (frame is TFrameType desiredFrame)
            {
                await writer.WriteAsync(desiredFrame, cancellationToken);
            }
        }
    }
}