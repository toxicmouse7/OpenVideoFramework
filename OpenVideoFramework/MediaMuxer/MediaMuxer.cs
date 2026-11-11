using FFmpeg.AutoGen.Abstractions;
using Microsoft.Extensions.Logging;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.MediaMuxer;

/// <summary>
/// Records one H.264 or MJPEG video track and one AC3 track into Matroska or MP4. Complete both inputs to finalize;
/// disposing an unfinished muxer aborts recording and releases its resources.
/// </summary>
public sealed unsafe class MediaMuxer : IDisposable
{
    private const int MaxStartupPackets = 256;
    private const int MaxPacketBytes = 32 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly MediaMuxerSettings _settings;
    private readonly string _formatName;
    private readonly Queue<PendingPacket> _startup = new(MaxStartupPackets);
    private readonly H264MuxerTrack _h264 = new();
    private AVFormatContext* _format;
    private AVStream* _video;
    private AVStream* _audio;
    private ILogger<MediaMuxer>? _logger;
    private int? _videoTrackId;
    private int? _audioTrackId;
    private int _startupPackets;
    private long _startupBytes;
    private bool _headerWritten;
    private bool _videoComplete;
    private bool _audioComplete;
    private bool _finished;
    private bool _disposed;
    private Exception? _failure;
    private long _origin;
    private long? _lastVideoDts;
    private long? _lastAudioDts;
    private long _videoDuration;

    public IPipelineSink<MediaSample<EncodedVideoFrame>> Video { get; }
    public IPipelineSink<MediaSample<AudioFrame>> Audio { get; }

    public MediaMuxer(MediaMuxerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.OutputPath))
        {
            throw new ArgumentException("MediaMuxer requires an output path.", nameof(settings));
        }

        _formatName = Path.GetExtension(settings.OutputPath).ToLowerInvariant() switch
        {
            ".mkv" => "matroska",
            ".mp4" => "mp4",
            _ => throw new ArgumentException("MediaMuxer requires a .mkv or .mp4 output path.", nameof(settings))
        };

        if (!Enum.IsDefined(settings.SynchronizationMode))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Unknown media synchronization mode.");
        }

        _settings = settings;
        Video = new Input<EncodedVideoFrame>(this, true, ConsumeVideo);
        Audio = new Input<AudioFrame>(this, false, ConsumeAudio);
    }

    private void Prepare(PipelineContext context, CancellationToken token)
    {
        lock (_gate)
        {
            EnsureActive(token);
            _logger ??= context.GetLogger<MediaMuxer>();
        }
    }

    private void Consume<TFrame>(
        MediaSample<TFrame> sample,
        Action<MediaSample<TFrame>> consume,
        bool video,
        CancellationToken token) where TFrame : CompleteFrame
    {
        lock (_gate)
        {
            EnsureActive(token);
            try
            {
                if (video ? _videoComplete : _audioComplete)
                {
                    throw new InvalidOperationException($"The {(video ? "video" : "audio")} input has completed.");
                }

                consume(sample);
            }
            catch (Exception ex)
            {
                Fail(ex);
                throw;
            }
        }
    }

    private void ConsumeVideo(MediaSample<EncodedVideoFrame> sample)
    {
        var frame = sample.Frame;
        if (frame.Codec is not (Codec.H264 or Codec.MJPEG) || frame.Width <= 0 || frame.Height <= 0)
        {
            throw new NotSupportedException("MediaMuxer requires H.264 or MJPEG video with valid dimensions.");
        }

        CheckStartupLimit(frame.Data.Length);
        EnsureFormat();
        var codec = FFmpegPacketAdapter.MapCodec(frame.Codec);
        if (_videoTrackId.HasValue && (_videoTrackId != sample.TrackId || _video->codecpar->codec_id != codec ||
            _video->codecpar->width != frame.Width || _video->codecpar->height != frame.Height))
        {
            throw new NotSupportedException("The video track, codec and dimensions must remain constant.");
        }

        if (!_videoTrackId.HasValue)
        {
            _videoTrackId = sample.TrackId;
            _video->codecpar->codec_id = codec;
            _video->codecpar->width = frame.Width;
            _video->codecpar->height = frame.Height;
        }

        var pts = sample.Pts;
        var dts = sample.Dts;
        var isKeyFrame = true;
        if (frame.Codec == Codec.H264)
        {
            var (hasPicture, isIdr) = _h264.Inspect(frame.Data.Span);
            if (!hasPicture)
            {
                TryInitialize();
                return;
            }

            // H.264 may reorder pictures, so its decoding timestamp must be supplied.
            if (!pts.HasValue || !dts.HasValue)
            {
                throw new NotSupportedException(
                    "H.264 muxing requires known PTS and DTS; DTS reconstruction is unsupported.");
            }

            isKeyFrame = isIdr;
        }
        else
        {
            // Every MJPEG frame is independently decodable, without reordering.
            pts ??= dts;
            dts ??= pts;
        }

        var timing = GetTiming(pts, dts, sample.WallClock);
        var packet = FFmpegPacketAdapter.ToPacket(frame);
        Submit(new PendingPacket((nint)packet, true, timing.Pts, timing.Dts, TimeSpan.Zero, isKeyFrame));
    }

    private void ConsumeAudio(MediaSample<AudioFrame> sample)
    {
        var frame = sample.Frame;
        if (frame.Codec != Codec.AC3 || frame.SampleRate is not (32000 or 44100 or 48000) ||
            frame.Channels is < 1 or > 6 || frame.Duration <= TimeSpan.Zero)
        {
            throw new NotSupportedException(
                "MediaMuxer requires AC3 audio with valid sample rate, channels and duration.");
        }

        CheckStartupLimit(frame.Data.Length);
        EnsureFormat();
        if (_audioTrackId.HasValue && (_audioTrackId != sample.TrackId ||
            _audio->codecpar->sample_rate != frame.SampleRate ||
            _audio->codecpar->ch_layout.nb_channels != frame.Channels))
        {
            throw new NotSupportedException("The audio track, sample rate and channel count must remain constant.");
        }

        if (!_audioTrackId.HasValue)
        {
            _audioTrackId = sample.TrackId;
            _audio->codecpar->sample_rate = frame.SampleRate;
            _audio->codecpar->bit_rate = frame.Bitrate;
            ffmpeg.av_channel_layout_default(&_audio->codecpar->ch_layout, frame.Channels);
        }

        // AC3 has no decoding/presentation reordering.
        var (pts, dts) = GetTiming(sample.Pts ?? sample.Dts, sample.Dts ?? sample.Pts, sample.WallClock);
        var packet = FFmpegPacketAdapter.ToPacket(frame.Data, true);
        Submit(new PendingPacket((nint)packet, false, pts, dts, frame.Duration, false));
    }

    private (long Pts, long Dts) GetTiming(TimeSpan? pts, TimeSpan? dts, DateTimeOffset? wallClock)
    {
        if (_settings.SynchronizationMode == MediaSynchronizationMode.SharedTimeline)
        {
            if (!pts.HasValue || !dts.HasValue)
            {
                throw new NotSupportedException(
                    "Shared-timeline muxing requires presentation and decoding timestamps.");
            }

            return (pts.Value.Ticks, dts.Value.Ticks);
        }

        if (!wallClock.HasValue)
        {
            throw new NotSupportedException("Wall-clock muxing requires a reliable WallClock on every media sample.");
        }

        if (!pts.HasValue && !dts.HasValue)
        {
            // Only codecs without reordering (AC3 or MJPEG) can reach this path.
            return (wallClock.Value.UtcTicks, wallClock.Value.UtcTicks);
        }

        if (!pts.HasValue || !dts.HasValue)
        {
            throw new NotSupportedException("The DTS-to-PTS offset is unknown.");
        }

        return (wallClock.Value.UtcTicks, checked(wallClock.Value.UtcTicks + (dts.Value - pts.Value).Ticks));
    }

    private void CheckStartupLimit(int size)
    {
        if (size <= 0 || size > MaxPacketBytes)
        {
            throw new ArgumentException("Encoded packets must contain between 1 byte and 32 MiB.");
        }

        if (!_headerWritten && (++_startupPackets > MaxStartupPackets || (_startupBytes += size) > MaxPacketBytes))
        {
            throw new InvalidOperationException(
                "Muxer initialization exceeded 256 samples or 32 MiB " +
                "while waiting for both tracks and video configuration (SPS/PPS/IDR for H.264).");
        }
    }

    private void EnsureFormat()
    {
        if (_format != null)
        {
            return;
        }

        fixed (AVFormatContext** format = &_format)
        {
            FFmpegPacketAdapter.Check(ffmpeg.avformat_alloc_output_context2(
                format, null, _formatName, _settings.OutputPath));
        }

        if (_format == null)
        {
            throw new OutOfMemoryException();
        }

        // Let FFmpeg interleave, but limit how far it waits for a sparse or completed peer.
        _format->max_interleave_delta = 1000000;
        _video = ffmpeg.avformat_new_stream(_format, null);
        _audio = ffmpeg.avformat_new_stream(_format, null);
        if (_video == null || _audio == null)
        {
            throw new OutOfMemoryException();
        }

        _video->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        _audio->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
        _audio->codecpar->codec_id = AVCodecID.AV_CODEC_ID_AC3;
        _video->time_base = _audio->time_base = new AVRational { num = 1, den = 1000 };
    }

    private void Submit(PendingPacket pending)
    {
        if (!_headerWritten)
        {
            _startup.Enqueue(pending);
            TryInitialize();
            return;
        }

        Write(pending);
    }

    private void TryInitialize()
    {
        if (_headerWritten || !_audioTrackId.HasValue ||
            !_startup.Any(packet => packet.IsVideo && packet.IsKeyFrame))
        {
            return;
        }

        if (_video->codecpar->codec_id == AVCodecID.AV_CODEC_ID_H264)
        {
            if (!_h264.HasConfiguration)
            {
                return;
            }

            SkipVideoPrefix();
            var firstVideo = _startup.First(packet => packet.IsVideo);
            _h264.Configure(_video->codecpar, (AVPacket*)firstVideo.Packet);
        }

        _origin = _startup.Min(packet => Math.Min(packet.Pts, packet.Dts));
        FFmpegPacketAdapter.Check(ffmpeg.avio_open(&_format->pb, _settings.OutputPath, ffmpeg.AVIO_FLAG_WRITE));
        FFmpegPacketAdapter.Check(ffmpeg.avformat_write_header(_format, null));
        _headerWritten = true;
        while (_startup.TryDequeue(out var pending))
        {
            Write(pending);
        }
    }

    private void SkipVideoPrefix()
    {
        // Discard only the undecodable video prefix; audio keeps its original timing.
        var started = false;
        var skipped = 0;
        var count = _startup.Count;
        for (var index = 0; index < count; index++)
        {
            var pending = _startup.Dequeue();
            if (pending.IsVideo && !started && !pending.IsKeyFrame)
            {
                FreePacket(pending);
                skipped++;
                continue;
            }

            started |= pending.IsVideo;
            _startup.Enqueue(pending);
        }

        if (skipped > 0)
        {
            _logger?.LogWarning("Skipped {Count} video pictures before the first decodable IDR.", skipped);
        }
    }

    private void Write(PendingPacket pending)
    {
        var packet = (AVPacket*)pending.Packet;
        try
        {
            if (pending.IsVideo)
            {
                if (_video->codecpar->codec_id == AVCodecID.AV_CODEC_ID_H264)
                {
                    _h264.Validate(packet, pending.IsKeyFrame);
                }

                if (pending.IsKeyFrame)
                {
                    packet->flags |= ffmpeg.AV_PKT_FLAG_KEY;
                }
                else
                {
                    packet->flags &= ~ffmpeg.AV_PKT_FLAG_KEY;
                }
            }

            var stream = pending.IsVideo ? _video : _audio;
            packet->stream_index = stream->index;
            packet->pts = FFmpegPacketAdapter.ToTimestamp(TimeSpan.FromTicks(checked(pending.Pts - _origin)),
                stream->time_base);
            packet->dts = FFmpegPacketAdapter.ToTimestamp(TimeSpan.FromTicks(checked(pending.Dts - _origin)),
                stream->time_base);
            packet->duration = FFmpegPacketAdapter.ToTimestamp(pending.Duration, stream->time_base);
            var last = pending.IsVideo ? _lastVideoDts : _lastAudioDts;
            if (packet->dts > packet->pts || (last.HasValue && packet->dts <= last.Value))
            {
                throw new NotSupportedException(
                    "Each track requires increasing DTS <= PTS representable in the output stream's time base.");
            }

            if (pending.IsVideo)
            {
                if (last.HasValue && _formatName == "mp4")
                {
                    // MP4 derives earlier sample durations from DTS; estimate the final one from this interval.
                    _videoDuration = checked(packet->dts - last.Value);
                    packet->duration = _videoDuration;
                }

                _lastVideoDts = packet->dts;
            }
            else
            {
                _lastAudioDts = packet->dts;
            }

            FFmpegPacketAdapter.Check(ffmpeg.av_interleaved_write_frame(_format, packet));
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
        }
    }

    private void Complete(bool video, CancellationToken token)
    {
        lock (_gate)
        {
            if (_finished)
            {
                return;
            }

            EnsureActive(token);
            try
            {
                if (video)
                {
                    _videoComplete = true;
                }
                else
                {
                    _audioComplete = true;
                }

                if (!_videoComplete || !_audioComplete)
                {
                    return;
                }

                if (!_headerWritten)
                {
                    throw new InvalidOperationException(
                        "Both tracks and a decodable video keyframe are required before muxing can complete.");
                }

                if (_formatName == "mp4" && _videoDuration == 0)
                {
                    throw new NotSupportedException(
                        "MP4 recording requires at least two video frames to determine the final frame duration.");
                }

                FFmpegPacketAdapter.Check(ffmpeg.av_interleaved_write_frame(_format, null));
                FFmpegPacketAdapter.Check(ffmpeg.av_write_trailer(_format));
                var result = ffmpeg.avio_closep(&_format->pb);
                Release();
                FFmpegPacketAdapter.Check(result);
                _finished = true;
            }
            catch (Exception ex)
            {
                Fail(ex);
                throw;
            }
        }
    }

    private void EnsureActive(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failure is not null)
        {
            throw new InvalidOperationException("The muxer has failed and cannot accept more input.", _failure);
        }

        if (token.IsCancellationRequested)
        {
            var error = new OperationCanceledException(token);
            Fail(error);
            throw error;
        }

        if (_finished)
        {
            throw new InvalidOperationException("The muxer has already completed.");
        }
    }

    private static void FreePacket(PendingPacket pending)
    {
        var packet = (AVPacket*)pending.Packet;
        ffmpeg.av_packet_free(&packet);
    }

    private void Fail(Exception error)
    {
        _failure ??= error;
        Release();
    }

    private void Release()
    {
        while (_startup.TryDequeue(out var pending))
        {
            FreePacket(pending);
        }

        _h264.Dispose();
        if (_format != null)
        {
            if (_format->pb != null)
            {
                ffmpeg.avio_closep(&_format->pb);
            }

            ffmpeg.avformat_free_context(_format);
            _format = null;
            _video = _audio = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                Release();
                _disposed = true;
            }
        }
    }

    private readonly record struct PendingPacket(
        nint Packet, bool IsVideo, long Pts, long Dts, TimeSpan Duration, bool IsKeyFrame);

    private sealed class Input<TFrame>(
        MediaMuxer owner, bool video, Action<MediaSample<TFrame>> consume)
        : IPipelineSink<MediaSample<TFrame>>, IDisposable where TFrame : CompleteFrame
    {
        public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
        {
            owner.Prepare(context, cancellationToken);
            return Task.CompletedTask;
        }

        public ValueTask ConsumeAsync(MediaSample<TFrame> input, CancellationToken cancellationToken)
        {
            owner.Consume(input, consume, video, cancellationToken);
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            owner.Complete(video, cancellationToken);
            return ValueTask.CompletedTask;
        }

        public void Dispose() => owner.Dispose();
    }
}
