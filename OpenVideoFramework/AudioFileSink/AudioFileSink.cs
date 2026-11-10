using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.AudioFileSink;

/// <summary>
/// Writes <see cref="AudioFrame"/> to containers (e.g. ac3) with automatic file rotation.
/// </summary>
public class AudioFileSink : IPipelineSink<MediaSample<AudioFrame>>, IDisposable
{
    private readonly AudioFileSinkSettings _settings;
    private unsafe AVFormatContext* _formatContext;
    private unsafe AVStream* _audioStream;
    private TimeSpan _nextPts;
    private TimeSpan _segmentStart;
    private TimeSpan? _lastPts;
    private long? _lastDts;
    private int? _trackId;

    public AudioFileSink(AudioFileSinkSettings settings) => _settings = settings;

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public ValueTask ConsumeAsync(MediaSample<AudioFrame> sample, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConsumeFrame(sample);
        return ValueTask.CompletedTask;
    }

    private unsafe void ConsumeFrame(MediaSample<AudioFrame> sample)
    {
        var frame = sample.Frame;
        if (_trackId.HasValue && _trackId != sample.TrackId)
        {
            throw new NotSupportedException("An audio file sink records one track. Use a separate sink per track.");
        }

        if (frame.SampleRate <= 0 || frame.Duration <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Audio recording requires a positive sample rate and frame duration.", nameof(sample));
        }

        _trackId = sample.TrackId;
        var pts = sample.Pts ?? sample.Dts ?? _nextPts;
        if (_lastPts.HasValue && pts <= _lastPts.Value)
        {
            throw new NotSupportedException("Audio presentation timestamps must increase.");
        }

        if (_formatContext == null || pts - _segmentStart >= _settings.RollPeriod)
        {
            RollFile(frame);
            _segmentStart = pts;
        }

        WritePacketToContainer(sample, pts);
    }

    private unsafe void WritePacketToContainer(MediaSample<AudioFrame> sample, TimeSpan pts)
    {
        var frame = sample.Frame;
        if (_formatContext == null)
        {
            return;
        }

        var packet = ffmpeg.av_packet_alloc();
        if (packet == null)
        {
            throw new OutOfMemoryException();
        }

        try
        {
            FFmpegPacketAdapter.Check(ffmpeg.av_new_packet(packet, frame.Data.Length));

            Marshal.Copy(frame.Data, 0, (IntPtr)packet->data, frame.Data.Length);

            packet->size = frame.Data.Length;
            packet->stream_index = _audioStream->index;
            packet->pts = FFmpegPacketAdapter.ToTimestamp(pts, _audioStream->time_base);
            // The only supported audio codec is AC3, which has no picture reordering.
            var dts = FFmpegPacketAdapter.ToTimestamp(sample.Dts ?? pts, _audioStream->time_base);
            packet->dts = dts;
            packet->duration = FFmpegPacketAdapter.ToTimestamp(frame.Duration, _audioStream->time_base);
            if (dts > packet->pts || (_lastDts.HasValue && dts <= _lastDts.Value) || packet->duration <= 0)
            {
                throw new NotSupportedException(
                    "Audio timestamps and duration cannot be represented correctly in the output stream.");
            }

            FFmpegPacketAdapter.Check(ffmpeg.av_interleaved_write_frame(_formatContext, packet));

            _lastDts = dts;
            _lastPts = pts;
            _nextPts = pts + frame.Duration;
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
        }
    }

    private unsafe void RollFile(AudioFrame frame)
    {
        if ((IntPtr)_formatContext != IntPtr.Zero)
        {
            ReleaseUnmanagedResources();
        }

        var extension = Path.GetExtension(_settings.OutputPath);
        var filename =
            Path.ChangeExtension(
                $"{Path.ChangeExtension(_settings.OutputPath, null)} {DateTime.Now:dd-MM-yy}",
                extension);

        if (File.Exists(filename))
        {
            var files = Directory.EnumerateFiles(
                Path.GetDirectoryName(filename)!,
                $"{Path.GetFileNameWithoutExtension(filename)}*");
            filename = Path.ChangeExtension($"{Path.ChangeExtension(filename, null)} ({files.Count()})", extension);
        }

        fixed (AVFormatContext** formatContextPtr = &_formatContext)
        {
            FFmpegPacketAdapter.Check(ffmpeg.avformat_alloc_output_context2(formatContextPtr, null, null, filename));
        }

        if (_formatContext == null)
        {
            throw new OutOfMemoryException();
        }

        _audioStream = ffmpeg.avformat_new_stream(_formatContext, null);
        if (_audioStream == null)
        {
            throw new OutOfMemoryException();
        }
        _audioStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
        _audioStream->codecpar->codec_id = MapCodec(frame.Codec);
        _audioStream->codecpar->sample_rate = frame.SampleRate;
        _audioStream->codecpar->ch_layout = GetChannelLayout(frame.Channels);
        _audioStream->codecpar->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLTP;
        _audioStream->codecpar->bit_rate = frame.Bitrate;
        _audioStream->time_base = ffmpeg.av_make_q(1, frame.SampleRate);

        FFmpegPacketAdapter.Check(ffmpeg.avio_open(&_formatContext->pb, filename, ffmpeg.AVIO_FLAG_WRITE));
        FFmpegPacketAdapter.Check(ffmpeg.avformat_write_header(_formatContext, null));
        _lastDts = null;
    }

    private static unsafe AVChannelLayout GetChannelLayout(int channelCount)
    {
        var layout = new AVChannelLayout();

        switch (channelCount)
        {
            case 6:
                ffmpeg.av_channel_layout_from_mask(&layout, ffmpeg.AV_CH_LAYOUT_5POINT1);
                break;
            default:
                ffmpeg.av_channel_layout_default(&layout, channelCount);
                break;
        }

        return layout;
    }

    private static AVCodecID MapCodec(Codec codec)
    {
        return codec switch
        {
            Codec.AC3 => AVCodecID.AV_CODEC_ID_AC3,
            _ => throw new NotSupportedException($"Codec \"{codec}\" not supported."),
        };
    }

    private unsafe void ReleaseUnmanagedResources()
    {
        if (_formatContext == null)
        {
            return;
        }

        ffmpeg.av_write_trailer(_formatContext);
        ffmpeg.avio_closep(&_formatContext->pb);
        ffmpeg.avformat_free_context(_formatContext);
        _formatContext = null;
        _audioStream = null;
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    public ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    ~AudioFileSink()
    {
        ReleaseUnmanagedResources();
    }
}
