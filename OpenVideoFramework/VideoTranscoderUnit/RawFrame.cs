using FFmpeg.AutoGen.Abstractions;
using OpenVideoFramework.Common;

namespace OpenVideoFramework.VideoTranscoderUnit;

internal sealed unsafe class RawFrame : IDisposable
{
    private AVFrame* _frame;
    internal AVFrame* AVFrame => _frame;
    internal TimeSpan? Pts => FFmpegPacketAdapter.FromTimestamp(_frame->pts, FFmpegPacketAdapter.TimeSpanTimeBase);
    internal DateTimeOffset? WallClock { get; }
    internal RawFrame(AVFrame* frame, DateTimeOffset? wallClock)
    {
        _frame = frame;
        WallClock = wallClock;
    }

    public void Dispose()
    {
        fixed (AVFrame** frame = &_frame)
        {
            ffmpeg.av_frame_free(frame);
        }
    }
}
