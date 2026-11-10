using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;
using OpenVideoFramework.Common;
using OpenVideoFramework.VideoTranscoderUnit.Exceptions;

namespace OpenVideoFramework.VideoTranscoderUnit;

internal sealed unsafe class Encoder : IDisposable
{
    private readonly Codec _codec;
    private AVCodecContext* _context;
    private SwsContext* _swsContext;
    private FFmpegBitstreamFilter? _filter;
    private readonly int _trackId;
    private readonly Dictionary<long, DateTimeOffset> _wallClocks = [];
    private long _nextPts;
    private long? _lastPts;
    private readonly long _frameDuration;

    internal Encoder(VideoTranscoderUnitSettings settings, RawFrame firstFrame, int trackId)
    {
        _trackId = trackId;
        _codec = settings.OutputCodec;
        var codec = settings.SpecificEncoder is null
            ? ffmpeg.avcodec_find_encoder(FFmpegPacketAdapter.MapCodec(_codec))
            : ffmpeg.avcodec_find_encoder_by_name(settings.SpecificEncoder);
        if (codec == null)
        {
            throw new EncoderNotFoundException();
        }

        if (codec->id != FFmpegPacketAdapter.MapCodec(_codec))
        {
            throw new ArgumentException("The selected encoder does not support the output codec.");
        }

        try
        {
            _context = ffmpeg.avcodec_alloc_context3(codec);
            if (_context == null)
            {
                throw new OutOfMemoryException();
            }

            _context->width = firstFrame.AVFrame->width;
            _context->height = firstFrame.AVFrame->height;
            _context->framerate = ffmpeg.av_d2q(settings.Fps, 1000000);
            _context->time_base = FFmpegPacketAdapter.TimeSpanTimeBase;
            _frameDuration = ffmpeg.av_rescale_q(1, ffmpeg.av_inv_q(_context->framerate), _context->time_base);
            if (_frameDuration <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.Fps));
            }

            _context->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            _context->max_b_frames = 0;
            _context->gop_size = 60;
            if (_codec == Codec.MJPEG)
            {
                _context->color_range = AVColorRange.AVCOL_RANGE_JPEG;
            }
            // No global-header flag: parameter sets must remain in-band at H.264 keyframes.
            FFmpegPacketAdapter.Check(ffmpeg.avcodec_open2(_context, codec, null));
            if (_context->has_b_frames != 0)
            {
                throw new NotSupportedException("The selected encoder must support encoding without B frames.");
            }

            if (_codec == Codec.H264)
            {
                var parameters = ffmpeg.avcodec_parameters_alloc();
                if (parameters == null)
                {
                    throw new OutOfMemoryException();
                }

                try
                {
                    FFmpegPacketAdapter.Check(ffmpeg.avcodec_parameters_from_context(parameters, _context));
                    _filter = new FFmpegBitstreamFilter("h264_mp4toannexb", parameters, _context->time_base);
                }
                finally { ffmpeg.avcodec_parameters_free(&parameters); }
            }
        }
        catch { Dispose(); throw; }
    }

    internal string? Name => Marshal.PtrToStringAnsi((IntPtr)_context->codec->long_name);

    internal List<MediaSample<EncodedVideoFrame>> Encode(RawFrame? raw)
    {
        AVFrame* converted = null;
        var frames = new List<MediaSample<EncodedVideoFrame>>();
        try
        {
            var source = raw is null ? null : raw.AVFrame;
            if (source != null)
            {
                if (source->width != _context->width || source->height != _context->height || source->format != (int)_context->pix_fmt)
                {
                    _swsContext = ffmpeg.sws_getCachedContext(_swsContext, source->width, source->height,
                        (AVPixelFormat)source->format, _context->width, _context->height, _context->pix_fmt,
                        (int)SwsFlags.SWS_BILINEAR, null, null, null);
                    if (_swsContext == null)
                    {
                        throw new SwsContextUnavailableException();
                    }

                    converted = ffmpeg.av_frame_alloc();
                    if (converted == null)
                    {
                        throw new OutOfMemoryException();
                    }

                    converted->width = _context->width;
                    converted->height = _context->height;
                    converted->format = (int)_context->pix_fmt;
                    FFmpegPacketAdapter.Check(ffmpeg.av_frame_get_buffer(converted, 0));
                    FFmpegPacketAdapter.Check(ffmpeg.sws_scale(_swsContext, source->data, source->linesize,
                        0, source->height, converted->data, converted->linesize));
                    source = converted;
                }
                var pts = raw!.Pts.HasValue
                    ? FFmpegPacketAdapter.ToTimestamp(raw.Pts, _context->time_base)
                    : _nextPts;
                if (_lastPts.HasValue && pts <= _lastPts.Value)
                {
                    throw new NotSupportedException(
                        "Decoded PTS must increase; the known timestamps conflict with preceding frames or the FPS fallback.");
                }

                if (raw.WallClock is { } wallClock)
                {
                    if (_wallClocks.Count >= 4096)
                    {
                        throw new NotSupportedException(
                            "The encoder has not returned timestamps for pending wall-clock associations.");
                    }

                    _wallClocks.Add(pts, wallClock);
                }

                _lastPts = pts;
                _nextPts = checked(pts + _frameDuration);
                source->pts = pts;
                source->pict_type = AVPictureType.AV_PICTURE_TYPE_NONE;
                source->duration = _frameDuration;
            }
            var result = ffmpeg.avcodec_send_frame(_context, source);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                Receive(frames);
                result = ffmpeg.avcodec_send_frame(_context, source);
            }
            FFmpegPacketAdapter.Check(result);
            Receive(frames);
            if (raw is null && _filter is not null)
            {
                _filter.Send(null);
                ReceiveFiltered(frames);
            }
            return frames;
        }
        finally { ffmpeg.av_frame_free(&converted); }
    }

    private void Receive(List<MediaSample<EncodedVideoFrame>> frames)
    {
        var packet = ffmpeg.av_packet_alloc();
        if (packet == null)
        {
            throw new OutOfMemoryException();
        }

        try
        {
            while (true)
            {
                var result = ffmpeg.avcodec_receive_packet(_context, packet);
                if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
                {
                    break;
                }

                FFmpegPacketAdapter.Check(result);
                if (_filter is null)
                {
                    frames.Add(Convert(packet, _context->time_base));
                }
                else
                {
                    _filter.Send(packet);
                    ReceiveFiltered(frames);
                }
                ffmpeg.av_packet_unref(packet);
            }
        }
        finally { ffmpeg.av_packet_free(&packet); }
    }

    private void ReceiveFiltered(List<MediaSample<EncodedVideoFrame>> frames)
    {
        var packet = ffmpeg.av_packet_alloc();
        if (packet == null)
        {
            throw new OutOfMemoryException();
        }

        try
        {
            while (_filter!.Receive(packet))
            {
                frames.Add(Convert(packet, _filter.TimeBase));
                ffmpeg.av_packet_unref(packet);
            }
        }
        finally { ffmpeg.av_packet_free(&packet); }
    }

    private MediaSample<EncodedVideoFrame> Convert(AVPacket* packet, AVRational timeBase)
    {
        DateTimeOffset? wallClock = null;
        if (packet->pts != ffmpeg.AV_NOPTS_VALUE &&
            _wallClocks.Remove(ffmpeg.av_rescale_q(packet->pts, timeBase, _context->time_base), out var time))
        {
            wallClock = time;
        }

        return new MediaSample<EncodedVideoFrame>(
            FFmpegPacketAdapter.FromPacket(packet, _codec, _context->width, _context->height), _trackId,
            FFmpegPacketAdapter.FromTimestamp(packet->pts, timeBase),
            FFmpegPacketAdapter.FromTimestamp(packet->dts, timeBase), wallClock);
    }

    public void Dispose()
    {
        _filter?.Dispose();
        fixed (AVCodecContext** context = &_context)
        {
            ffmpeg.avcodec_free_context(context);
        }

        ffmpeg.sws_freeContext(_swsContext);
        _swsContext = null;
    }
}
