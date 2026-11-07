using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;
using OpenVideoFramework.Common;
using OpenVideoFramework.VideoTranscoderUnit.Exceptions;

namespace OpenVideoFramework.VideoTranscoderUnit;

internal sealed unsafe class Decoder : IDisposable
{
    private AVCodecContext* _context;

    internal Decoder(EncodedVideoFrame frame, string? specificDecoder)
    {
        var codec = specificDecoder is null
            ? ffmpeg.avcodec_find_decoder(FFmpegPacketAdapter.MapCodec(frame.Codec))
            : ffmpeg.avcodec_find_decoder_by_name(specificDecoder);
        if (codec == null)
        {
            throw new DecoderNotFoundException();
        }

        if (codec->id != FFmpegPacketAdapter.MapCodec(frame.Codec))
        {
            throw new ArgumentException("The selected decoder does not support the input codec.");
        }

        try
        {
            _context = ffmpeg.avcodec_alloc_context3(codec);
            if (_context == null)
            {
                throw new OutOfMemoryException();
            }

            _context->width = frame.Width;
            _context->height = frame.Height;
            FFmpegPacketAdapter.Check(ffmpeg.avcodec_open2(_context, codec, null));
        }
        catch { Dispose(); throw; }
    }

    internal string? Name => Marshal.PtrToStringAnsi((IntPtr)_context->codec->long_name);

    internal List<RawFrame> Decode(EncodedVideoFrame? frame)
    {
        var packet = frame is null ? null : FFmpegPacketAdapter.ToPacket(frame);
        var frames = new List<RawFrame>();
        try
        {
            var result = ffmpeg.avcodec_send_packet(_context, packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                Receive(frames);
                result = ffmpeg.avcodec_send_packet(_context, packet);
            }
            FFmpegPacketAdapter.Check(result);
            Receive(frames);
            return frames;
        }
        catch
        {
            foreach (var raw in frames)
            {
                raw.Dispose();
            }

            throw;
        }
        finally { ffmpeg.av_packet_free(&packet); }
    }

    private void Receive(List<RawFrame> frames)
    {
        var frame = ffmpeg.av_frame_alloc();
        if (frame == null)
        {
            throw new OutOfMemoryException();
        }

        try
        {
            while (true)
            {
                var result = ffmpeg.avcodec_receive_frame(_context, frame);
                if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
                {
                    break;
                }

                FFmpegPacketAdapter.Check(result);
                var clone = ffmpeg.av_frame_clone(frame);
                if (clone == null)
                {
                    throw new OutOfMemoryException();
                }

                frames.Add(new RawFrame(clone));
                ffmpeg.av_frame_unref(frame);
            }
        }
        finally { ffmpeg.av_frame_free(&frame); }
    }

    public void Dispose()
    {
        fixed (AVCodecContext** context = &_context)
        {
            ffmpeg.avcodec_free_context(context);
        }
    }
}
