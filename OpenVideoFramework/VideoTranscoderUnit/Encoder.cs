using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;
using OpenVideoFramework.Common;
using OpenVideoFramework.VideoTranscoderUnit.Exceptions;

namespace OpenVideoFramework.VideoTranscoderUnit;

internal class Encoder : IDisposable
{
    private readonly Codec _codec;
    private readonly unsafe AVCodecContext* _codecContext;
    private readonly unsafe SwsContext* _swsContext;
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

        _swsContext = ffmpeg.sws_getContext(
            width, height, format,
            width, height, AVPixelFormat.AV_PIX_FMT_YUVJ420P,
            ffmpeg.SWS_BILINEAR, null, null, null);

        if (_swsContext == null)
        {
            throw new SwsContextUnavailableException();
        }

        _codecContext = ffmpeg.avcodec_alloc_context3(ffmpegCodec);

        _codecContext->width = width;
        _codecContext->height = height;
        _codecContext->time_base = ffmpeg.av_make_q(1, 1);
        _codecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUVJ420P;

        ffmpeg.avcodec_open2(_codecContext, ffmpegCodec, null);
    }

    public unsafe string? Name => Marshal.PtrToStringAnsi((IntPtr)_codecContext->codec->long_name);

    public unsafe VideoFrame[] Encode(RawFrame frame)
    {
        var frames = Enumerable.Empty<VideoFrame>();
        var avFrame = frame.AVFrame;
        avFrame->pts = _frameCounter++;

        if (avFrame->format != (int)AVPixelFormat.AV_PIX_FMT_YUVJ420P)
        {
            var convertedFrame = ffmpeg.av_frame_alloc();
            ffmpeg.sws_scale_frame(_swsContext, convertedFrame, avFrame);
            convertedFrame->duration = avFrame->duration;
            
            ffmpeg.av_frame_unref(avFrame);
            avFrame = convertedFrame;
        }

        ffmpeg.avcodec_send_frame(_codecContext, avFrame);

        var packet = ffmpeg.av_packet_alloc();

        while (ffmpeg.avcodec_receive_packet(_codecContext, packet) >= 0)
        {
            frames = frames.Append(Utils.AVPacketToVideoFrame(
                packet, null, frame.ClockRate, _codec,
                avFrame->width, avFrame->height,
                frame.ReceivedAt, TimeSpan.FromSeconds(avFrame->duration / (double)frame.ClockRate)));

            ffmpeg.av_frame_unref(avFrame);
        }

        ffmpeg.av_packet_free(&packet);

        return frames.ToArray();
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

    private unsafe void ReleaseUnmanagedResources()
    {
        ffmpeg.sws_freeContext(_swsContext);
        fixed (AVCodecContext** codecContext = &_codecContext)
        {
            ffmpeg.avcodec_free_context(codecContext);
        }
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    ~Encoder()
    {
        ReleaseUnmanagedResources();
    }
}