using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;

namespace OpenVideoFramework.Common;

public static class Utils
{
    public static unsafe VideoFrame AVPacketToVideoFrame(
        AVPacket* avPacket,
        uint clockRate,
        Codec codec,
        int width,
        int height,
        DateTimeOffset receivedAt,
        TimeSpan duration = default)
    {
        var buffer = new byte[avPacket->size];
        Marshal.Copy((IntPtr)avPacket->data, buffer, 0, buffer.Length);

        return new VideoFrame
        {
            Data = buffer,
            ClockRate = clockRate,
            Codec = codec,
            Duration = duration,
            IsKeyFrame = (avPacket->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0,
            ReceivedAt = receivedAt,
            Height = height,
            Width = width
        };
    }
    
    public static Codec MapCodec(AVCodecID codec)
    {
        return codec switch
        {
            AVCodecID.AV_CODEC_ID_MJPEG => Codec.MJPEG,
            AVCodecID.AV_CODEC_ID_H264 => Codec.H264,
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported codec.")
        };
    }
}