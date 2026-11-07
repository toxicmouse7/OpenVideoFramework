using FFmpeg.AutoGen.Abstractions;

namespace OpenVideoFramework.VideoTranscoderUnit;

internal sealed unsafe class RawFrame : IDisposable
{
    private AVFrame* _frame;
    internal AVFrame* AVFrame => _frame;
    internal RawFrame(AVFrame* frame) => _frame = frame;

    public void Dispose()
    {
        fixed (AVFrame** frame = &_frame)
        {
            ffmpeg.av_frame_free(frame);
        }
    }
}
