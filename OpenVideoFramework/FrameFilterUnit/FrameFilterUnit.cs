using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.FrameFilterUnit;

/// <summary>
/// Filters <see cref="CompleteFrame"/> based on generic type.
/// </summary>
/// <typeparam name="TFrameType">>Type to filter on. Must be a derived type from <see cref="CompleteFrame"/></typeparam>
public class FrameFilterUnit<TFrameType> : IPipelineUnit<MediaSample<CompleteFrame>, MediaSample<TFrameType>>
    where TFrameType : CompleteFrame
{
    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async ValueTask ProcessAsync(
        MediaSample<CompleteFrame> input,
        Emit<MediaSample<TFrameType>> emit,
        CancellationToken cancellationToken)
    {
        if (input.Frame is TFrameType desiredFrame)
        {
            await emit(new MediaSample<TFrameType>(desiredFrame, input.TrackId, input.Pts, input.Dts, input.WallClock),
                cancellationToken);
        }
    }
}
