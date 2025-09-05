using System.Runtime.InteropServices;
using System.Threading.Channels;
using FFmpeg.AutoGen.Abstractions;
using OpenVideoFramework.FrameAssemblerUnit;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtpDemuxUnit;

namespace OpenVideoFramework.VideoFileSink;

public class VideoFileSinkSettings
{
    public Codec Codec { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double Fps { get; init; }
    public TimeSpan RollPeriod { get; init; }
}

public class VideoFileSink : IPipelineSink<CompleteFrame>, IDisposable
{
    private readonly VideoFileSinkSettings _settings;
    private unsafe AVFormatContext* _formatContext;
    private unsafe AVStream* _videoStream;
    private int _frameCount;

    public VideoFileSink(VideoFileSinkSettings settings)
    {
        _settings = settings;
    }

    public Task PrepareForExecutionAsync(CancellationToken cancellationToken)
    {
        RollFile();
        
        return Task.CompletedTask;
    }

    public async Task ConsumeAsync(ChannelReader<CompleteFrame> input, CancellationToken cancellationToken)
    {
        await foreach (var frame in input.ReadAllAsync(cancellationToken))
        {
            unsafe
            {
                var packet = ffmpeg.av_packet_alloc();
                ffmpeg.av_new_packet(packet, frame.Data.Length);

                Marshal.Copy(frame.Data, 0, (IntPtr)packet->data, frame.Data.Length);

                packet->stream_index = _videoStream->index;
                packet->pts = packet->dts = ffmpeg.av_rescale_q(
                    _frameCount,
                    ffmpeg.av_inv_q(_videoStream->r_frame_rate),
                    _videoStream->time_base);
                
                packet->duration = ffmpeg.av_rescale_q(
                    1,
                    ffmpeg.av_inv_q(_videoStream->r_frame_rate),
                    _videoStream->time_base);
                
                _videoStream->duration += packet->duration;

                ffmpeg.av_interleaved_write_frame(_formatContext, packet);
                ffmpeg.av_packet_free(&packet);

                var durationInSeconds = _videoStream->duration * ffmpeg.av_q2d(_videoStream->time_base);

                if (durationInSeconds >= _settings.RollPeriod.TotalSeconds)
                {
                    RollFile();
                }

                _frameCount++;
            }
        }
    }

    private unsafe void RollFile()
    {
        if ((IntPtr)_formatContext != IntPtr.Zero)
        {
            ReleaseUnmanagedResources();
        }

        var extension = Path.GetExtension("video.mp4");
        var filename = Path.ChangeExtension($"{Path.ChangeExtension("video.mp4", null)} {DateTime.Now:dd-MM-yy}", extension);

        if (File.Exists(filename))
        {
            var files = Directory.EnumerateFiles(".", $"{Path.ChangeExtension(filename, null)}*");
            filename = Path.ChangeExtension($"{Path.ChangeExtension(filename, null)} ({files.Count()})", extension);
        }
        
        
        fixed (AVFormatContext** formatContextPtr = &_formatContext)
        {
            ffmpeg.avformat_alloc_output_context2(formatContextPtr, null, null, filename);
        }

        _videoStream = ffmpeg.avformat_new_stream(_formatContext, null);
        _videoStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        _videoStream->codecpar->codec_id = MapCodec(_settings.Codec);
        _videoStream->codecpar->width = _settings.Width;
        _videoStream->codecpar->height = _settings.Height;
        _videoStream->time_base = ffmpeg.av_inv_q(ffmpeg.av_d2q(_settings.Fps, int.MaxValue));
        _videoStream->avg_frame_rate = ffmpeg.av_inv_q(_videoStream->time_base);
        _videoStream->r_frame_rate = ffmpeg.av_inv_q(_videoStream->time_base);
        _videoStream->duration = 0;

        ffmpeg.avio_open(&_formatContext->pb, filename, ffmpeg.AVIO_FLAG_WRITE);
        ffmpeg.avformat_write_header(_formatContext, null);
    }

    private static AVCodecID MapCodec(Codec codec)
    {
        return codec switch
        {
            Codec.MJPEG => AVCodecID.AV_CODEC_ID_MJPEG,
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported codec"),
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