using System.Runtime.InteropServices;
using System.Threading.Channels;
using FFmpeg.AutoGen.Abstractions;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.AudioFileSink;

/// <summary>
/// Writes <see cref="AudioFrame"/> to containers (e.g. ac3) with automatic file rotation.
/// </summary>
public class AudioFileSink : IPipelineSink<AudioFrame>, IDisposable
{
    private readonly AudioFileSinkSettings _settings;
    private unsafe AVFormatContext* _formatContext;
    private unsafe AVStream* _audioStream;
    private TimeSpan _totalDuration;

    public AudioFileSink(AudioFileSinkSettings settings)
    {
        _settings = settings;
    }

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ConsumeAsync(ChannelReader<AudioFrame> input, CancellationToken cancellationToken)
    {
        RuntimeInitialize(await input.ReadAsync(cancellationToken));

        await foreach (var frame in input.ReadAllAsync(cancellationToken))
        {
            if (_totalDuration >= _settings.RollPeriod)
            {
                RollFile(frame);
            }

            WritePacketToContainer(frame);
        }

        ReleaseUnmanagedResources();
    }

    private void RuntimeInitialize(AudioFrame frame)
    {
        RollFile(frame);
        WritePacketToContainer(frame);
    }

    private unsafe void WritePacketToContainer(AudioFrame frame)
    {
        if (_formatContext == null) return;

        var packet = ffmpeg.av_packet_alloc();
        try
        {
            ffmpeg.av_new_packet(packet, frame.Data.Length);

            Marshal.Copy(frame.Data, 0, (IntPtr)packet->data, frame.Data.Length);

            packet->size = frame.Data.Length;
            packet->stream_index = _audioStream->index;

            ffmpeg.av_interleaved_write_frame(_formatContext, packet);

            _totalDuration += frame.Duration;
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
            _totalDuration = TimeSpan.Zero;
        }

        var extension = Path.GetExtension(_settings.OutputPath);
        var filename =
            Path.ChangeExtension(
                $"{Path.ChangeExtension(_settings.OutputPath, null)} {DateTime.Now:dd-MM-yy}", extension);

        if (File.Exists(filename))
        {
            var files = Directory.EnumerateFiles(
                Path.GetDirectoryName(filename)!,
                $"{Path.GetFileNameWithoutExtension(filename)}*");
            filename = Path.ChangeExtension($"{Path.ChangeExtension(filename, null)} ({files.Count()})", extension);
        }

        fixed (AVFormatContext** formatContextPtr = &_formatContext)
        {
            ffmpeg.avformat_alloc_output_context2(formatContextPtr, null, null, filename);
        }

        _audioStream = ffmpeg.avformat_new_stream(_formatContext, null);
        _audioStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
        _audioStream->codecpar->codec_id = MapCodec(frame.Codec);
        _audioStream->codecpar->sample_rate = frame.SampleRate;
        _audioStream->codecpar->ch_layout = GetChannelLayout(frame.Channels);
        _audioStream->codecpar->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLTP;
        _audioStream->codecpar->bit_rate = frame.Bitrate;
        _audioStream->time_base = ffmpeg.av_make_q(1, frame.SampleRate);

        ffmpeg.avio_open(&_formatContext->pb, filename, ffmpeg.AVIO_FLAG_WRITE);
        ffmpeg.avformat_write_header(_formatContext, null);
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
        ffmpeg.av_write_trailer(_formatContext);
        ffmpeg.avio_closep(&_formatContext->pb);
        ffmpeg.avformat_free_context(_formatContext);
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    ~AudioFileSink()
    {
        ReleaseUnmanagedResources();
    }
}