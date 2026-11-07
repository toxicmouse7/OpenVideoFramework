using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;

namespace OpenVideoFramework.Common;

internal static unsafe class FFmpegPacketAdapter
{
    internal static void Check(int result)
    {
        if (result >= 0)
        {
            return;
        }

        var buffer = stackalloc byte[256];
        ffmpeg.av_strerror(result, buffer, 256);
        throw new InvalidOperationException($"FFmpeg: {Marshal.PtrToStringAnsi((IntPtr)buffer)} ({result}).");
    }

    internal static AVPacket* ToPacket(EncodedVideoFrame frame)
    {
        var packet = ffmpeg.av_packet_alloc();
        if (packet == null)
        {
            throw new OutOfMemoryException();
        }

        try
        {
            Check(ffmpeg.av_new_packet(packet, frame.Data.Length));
            frame.Data.Span.CopyTo(new Span<byte>(packet->data, packet->size));
            if (frame.IsKeyFrame)
            {
                packet->flags |= ffmpeg.AV_PKT_FLAG_KEY;
            }

            return packet;
        }
        catch
        {
            ffmpeg.av_packet_free(&packet);
            throw;
        }
    }

    internal static EncodedVideoFrame FromPacket(AVPacket* packet, Codec codec, int width, int height)
    {
        var data = new byte[packet->size];
        Marshal.Copy((IntPtr)packet->data, data, 0, data.Length);
        return new EncodedVideoFrame
        {
            Width = width,
            Height = height,
            Codec = codec,
            Data = data,
            IsKeyFrame = (packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0
        };
    }

    internal static AVCodecID MapCodec(Codec codec) => codec switch
    {
        Codec.MJPEG => AVCodecID.AV_CODEC_ID_MJPEG,
        Codec.H264 => AVCodecID.AV_CODEC_ID_H264,
        _ => throw new NotSupportedException($"Unsupported video codec: {codec}.")
    };

    internal static Codec MapCodec(AVCodecID codec) => codec switch
    {
        AVCodecID.AV_CODEC_ID_MJPEG => Codec.MJPEG,
        AVCodecID.AV_CODEC_ID_H264 => Codec.H264,
        _ => throw new NotSupportedException($"Unsupported video codec: {codec}.")
    };
}
