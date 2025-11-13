using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;
using OpenVideoFramework.Common;
using OpenVideoFramework.VideoTranscoderUnit.Exceptions;

namespace OpenVideoFramework.VideoTranscoderUnit;

internal class Decoder : IDisposable
{
    private readonly unsafe AVCodecContext* _codecContext;

    public unsafe Decoder(VideoFrame frame, Codec codec, int width, int height, string? specificDecoder)
    {
        var ffmpegCodec = specificDecoder is null
            ? ffmpeg.avcodec_find_decoder(MapCodec(codec))
            : ffmpeg.avcodec_find_decoder_by_name(specificDecoder);

        if ((IntPtr)ffmpegCodec == IntPtr.Zero)
        {
            throw new DecoderNotFoundException();
        }
        
        _codecContext = ffmpeg.avcodec_alloc_context3(ffmpegCodec);
        if (frame.ExtraData is not null)
        {
            _codecContext->extradata = (byte*)ffmpeg.av_malloc((uint)frame.ExtraData.Length);
            _codecContext->extradata_size = frame.ExtraData.Length;
            Marshal.Copy(frame.ExtraData, 0, (IntPtr)_codecContext->extradata, _codecContext->extradata_size);
        }
        
        _codecContext->width = width;
        _codecContext->height = height;

        ffmpeg.avcodec_open2(_codecContext, ffmpegCodec, null);
    }

    public unsafe string? Name => Marshal.PtrToStringAnsi((IntPtr)_codecContext->codec->long_name);

    public unsafe RawFrame[] Decode(VideoFrame frame)
    {
        var packet = VideoFrameToAVPacket(frame);
        var frames = Enumerable.Empty<RawFrame>();
        var errorCode = ffmpeg.avcodec_send_packet(_codecContext, packet);

        var avFrame = ffmpeg.av_frame_alloc();

        while (ffmpeg.avcodec_receive_frame(_codecContext, avFrame) >= 0)
        {
            frames = frames.Append(new RawFrame
            { 
                AVFrame = ffmpeg.av_frame_clone(avFrame),
                ReceivedAt = frame.ReceivedAt,
                ClockRate = frame.ClockRate
            });
            
            ffmpeg.av_frame_unref(avFrame);
        }
        
        ffmpeg.av_frame_free(&avFrame);
        ffmpeg.av_packet_free(&packet);

        return frames.ToArray();
    }

    private static unsafe AVPacket* VideoFrameToAVPacket(VideoFrame frame)
    {
        var packet = ffmpeg.av_packet_alloc();
        ffmpeg.av_new_packet(packet, frame.Data.Length);

        Marshal.Copy(frame.Data, 0, (IntPtr)packet->data, frame.Data.Length);
        if (frame.IsKeyFrame)
        {
            packet->flags |= ffmpeg.AV_PKT_FLAG_KEY;
        }

        packet->duration = (int)(frame.Duration.TotalSeconds * frame.ClockRate);

        return packet;
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

    public unsafe void Dispose()
    {
        fixed (AVCodecContext** codecContext = &_codecContext)
        {
            ffmpeg.avcodec_free_context(codecContext);
        }
    }
}