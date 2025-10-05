using FFmpeg.AutoGen.Abstractions;

namespace OpenVideoFramework.VideoTranscoderUnit;

internal class RawFrame : IDisposable
{
    private unsafe AVFrame* _avFrame;

    public required uint ClockRate { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public unsafe required AVFrame* AVFrame
    {
        get => _avFrame;
        init => _avFrame = value;
    }
    
    public unsafe AVPixelFormat PixelFormat => (AVPixelFormat)_avFrame->format;

    public unsafe void Dispose()
    {
        fixed (AVFrame** frame = &_avFrame)
        {
            ffmpeg.av_frame_free(frame);
        }

        GC.SuppressFinalize(this);
    }

    unsafe ~RawFrame()
    {
        fixed (AVFrame** frame = &_avFrame)
        {
            ffmpeg.av_frame_free(frame);
        }
    }
}