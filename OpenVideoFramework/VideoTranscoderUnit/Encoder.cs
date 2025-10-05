using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;
using OpenVideoFramework.Common;
using OpenVideoFramework.VideoTranscoderUnit.Exceptions;

namespace OpenVideoFramework.VideoTranscoderUnit;

internal class Encoder
{
    private readonly Codec _codec;
    private readonly unsafe AVCodecContext* _codecContext;
    private long _frameCounter;

    public unsafe Encoder(
        Codec codec,
        int width,
        int height,
        AVPixelFormat format,
        string? specificEncoder)
    {
        _codec = codec;
        var ffmpegCodec = specificEncoder is null
            ? ffmpeg.avcodec_find_encoder(MapCodec(codec))
            : ffmpeg.avcodec_find_encoder_by_name(specificEncoder);

        if ((IntPtr)ffmpegCodec == IntPtr.Zero)
        {
            throw new EncoderNotFoundException();
        }

        _codecContext = ffmpeg.avcodec_alloc_context3(ffmpegCodec);

        _codecContext->width = width;
        _codecContext->height = height;
        _codecContext->time_base = ffmpeg.av_make_q(1, 1);
        _codecContext->pix_fmt = format;

        ffmpeg.avcodec_open2(_codecContext, ffmpegCodec, null);
    }

    public unsafe string? Name => Marshal.PtrToStringAnsi((IntPtr)_codecContext->codec->long_name);

    public unsafe VideoFrame[] Encode(RawFrame frame)
    {
        var frames = Enumerable.Empty<VideoFrame>();
        var avFrame = frame.AVFrame;
        avFrame->pts = _frameCounter++;

        ffmpeg.avcodec_send_frame(_codecContext, avFrame);

        var packet = ffmpeg.av_packet_alloc();

        while (ffmpeg.avcodec_receive_packet(_codecContext, packet) >= 0)
        {
            frames = frames.Append(AVPacketToVideoFrame(packet, frame));

            ffmpeg.av_frame_unref(avFrame);
        }

        ffmpeg.av_packet_free(&packet);

        return frames.ToArray();
    }

    private unsafe VideoFrame AVPacketToVideoFrame(AVPacket* avPacket, RawFrame frame)
    {
        var buffer = new byte[avPacket->size];
        Marshal.Copy((IntPtr)avPacket->data, buffer, 0, buffer.Length);

        return new VideoFrame
        {
            Data = buffer,
            ClockRate = frame.ClockRate,
            Codec = _codec,
            Duration = TimeSpan.Zero,
            IsKeyFrame = (avPacket->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0,
            ReceivedAt = frame.ReceivedAt,
            Height = frame.AVFrame->height,
            Width = frame.AVFrame->width
        };
    }

    private static AVCodecID MapCodec(Codec codec)
    {
        return codec switch
        {
            Codec.MJPEG => AVCodecID.AV_CODEC_ID_MJPEG,
            Codec.H264 => AVCodecID.AV_CODEC_ID_H264,
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported codec.")
        };
    }
}