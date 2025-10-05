using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using FFmpeg.AutoGen.Abstractions;
using Microsoft.Extensions.Logging;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.VideoFileSink;

/// <summary>
/// Writes <see cref="VideoFrame"/> to containers (e.g. mp4) with automatic file rotation.
/// </summary>
public class VideoFileSink : IPipelineSink<VideoFrame>, IDisposable
{
    private readonly VideoFileSinkSettings _settings;
    private ILogger<VideoFileSink> _logger = null!;
    private unsafe AVFormatContext* _formatContext;
    private unsafe AVStream* _videoStream;
    private int _frameCount;
    private double _frameRate;

    public VideoFileSink(VideoFileSinkSettings settings)
    {
        _settings = settings;
    }

    private unsafe TimeSpan Duration => 
        TimeSpan.FromSeconds(_videoStream->duration * ffmpeg.av_q2d(_videoStream->time_base));

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        _logger = context.GetLogger<VideoFileSink>();
        
        return Task.CompletedTask;
    }

    public async Task ConsumeAsync(ChannelReader<VideoFrame> input, CancellationToken cancellationToken)
    {
        await RuntimeInitializeAsync(input, cancellationToken);

        await foreach (var frame in input.ReadAllAsync(cancellationToken))
        {
            if (Duration >= _settings.RollPeriod && frame.IsKeyFrame)
            {
                RollFile(frame);
            }

            WriteVideoFrame(frame);
        }
    }

    private async Task RuntimeInitializeAsync(ChannelReader<VideoFrame> input, CancellationToken cancellationToken)
    {
        if (_settings.ConstantFps is null)
        {
            _frameRate = await EstimateFrameRateAsync(input, cancellationToken);
        }
        else
        {
            _frameRate = _settings.ConstantFps.Value;
        }
        
        var frame = await input.ReadAsync(cancellationToken);
        
        RollFile(frame);
    }

    private unsafe void WriteVideoFrame(VideoFrame frame)
    {
        var packet = ffmpeg.av_packet_alloc();
        ffmpeg.av_new_packet(packet, frame.Data.Length);

        Marshal.Copy(frame.Data, 0, (IntPtr)packet->data, frame.Data.Length);

        packet->stream_index = _videoStream->index;
        var frameIntervalTicks = (long)(_videoStream->time_base.den / _frameRate);
        packet->pts = packet->dts = _frameCount * frameIntervalTicks;
        packet->duration = frameIntervalTicks;

        _videoStream->duration += packet->duration;

        ffmpeg.av_interleaved_write_frame(_formatContext, packet);
        ffmpeg.av_packet_free(&packet);
        
        _frameCount++;
    }

    private async Task<double> EstimateFrameRateAsync(
        ChannelReader<VideoFrame> input,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Waiting for video frames to estimate framerate.");
        await input.WaitToReadAsync(cancellationToken);
        _logger.LogInformation("Estimation started.");

        var framesCount = 0;
        var sw = Stopwatch.StartNew();
        
        await foreach (var _ in input.ReadAllAsync(cancellationToken))
        {
            framesCount++;
            if (sw.Elapsed.TotalSeconds < 10) continue;

            sw.Stop();
            break;
        }
        
        var frameRate = framesCount / sw.Elapsed.TotalSeconds;

        _logger.LogInformation("Estimated frame rate: {frameRate}.", frameRate);
        
        return frameRate;
    }

    private unsafe void RollFile(VideoFrame frame)
    {
        if ((IntPtr)_formatContext != IntPtr.Zero)
        {
            ReleaseUnmanagedResources();
            _frameCount = 0;
        }

        var extension = Path.GetExtension(_settings.OutputPath);
        var filename = Path.ChangeExtension(
            $"{Path.ChangeExtension(_settings.OutputPath, null)} {DateTime.Now:dd-MM-yy}", extension);

        if (File.Exists(filename))
        {
            var directory = Path.GetDirectoryName(filename);
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = ".";
            }

            var pattern =  $"{Path.GetFileNameWithoutExtension(filename)}*";
            var files = Directory.EnumerateFiles(directory, pattern);
            filename = Path.ChangeExtension($"{Path.ChangeExtension(filename, null)} ({files.Count()})", extension);
        }


        fixed (AVFormatContext** formatContextPtr = &_formatContext)
        {
            ffmpeg.avformat_alloc_output_context2(formatContextPtr, null, null, filename);
        }

        _videoStream = ffmpeg.avformat_new_stream(_formatContext, null);
        _videoStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        _videoStream->codecpar->codec_id = MapCodec(frame.Codec);
        _videoStream->codecpar->width = frame.Width;
        _videoStream->codecpar->height = frame.Height;
        _videoStream->time_base = ffmpeg.av_make_q(1, (int)frame.ClockRate);
        _videoStream->duration = 0;

        ffmpeg.avio_open(&_formatContext->pb, filename, ffmpeg.AVIO_FLAG_WRITE);
        ffmpeg.avformat_write_header(_formatContext, null);
    }

    private static AVCodecID MapCodec(Codec codec)
    {
        return codec switch
        {
            Codec.MJPEG => AVCodecID.AV_CODEC_ID_MJPEG,
            Codec.H264 => AVCodecID.AV_CODEC_ID_H264,
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported codec."),
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

    ~VideoFileSink()
    {
        ReleaseUnmanagedResources();
    }
}