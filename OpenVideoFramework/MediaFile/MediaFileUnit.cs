using FFmpeg.AutoGen.Abstractions;
using Microsoft.Extensions.Logging;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.MediaFile;

/// <summary>Consumes paths to media files and produces video frames.</summary>
public class MediaFileUnit : IPipelineUnit<string, CompleteFrame>
{
    private ILogger<MediaFileUnit> _logger = null!;

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        _logger = context.GetLogger<MediaFileUnit>();
        return Task.CompletedTask;
    }

    public async ValueTask ProcessAsync(string input, Emit<CompleteFrame> emit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new MediaReader(input);
        while (reader.ReadNext(cancellationToken) is { } frame)
            await emit(frame, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Media file processed. Media file: {path}.", input);
    }

    // Keep native pointers outside the asynchronous method; each packet is copied before emitting.
    private sealed unsafe class MediaReader : IDisposable
    {
        private AVFormatContext* _context;
        private AVPacket* _packet;

        public MediaReader(string path)
        {
            fixed (AVFormatContext** context = &_context)
            {
                if (ffmpeg.avformat_open_input(context, path, null, null) < 0)
                    throw new IOException($"Cannot open media file: {path}");
            }
            ffmpeg.avformat_find_stream_info(_context, null);
            _packet = ffmpeg.av_packet_alloc();
        }

        public VideoFrame? ReadNext(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ffmpeg.av_read_frame(_context, _packet) < 0) return null;
                try
                {
                    var stream = _context->streams[_packet->stream_index];
                    if (stream->codecpar->codec_type != AVMediaType.AVMEDIA_TYPE_VIDEO) continue;
                    return Utils.AVPacketToVideoFrame(
                        _packet, stream->codecpar, (uint)stream->time_base.den,
                        Utils.MapCodec(stream->codecpar->codec_id), stream->codecpar->width,
                        stream->codecpar->height, DateTimeOffset.Now,
                        TimeSpan.FromSeconds(_packet->duration * ffmpeg.av_q2d(stream->time_base)));
                }
                finally { ffmpeg.av_packet_unref(_packet); }
            }
        }

        public void Dispose()
        {
            fixed (AVPacket** packet = &_packet) ffmpeg.av_packet_free(packet);
            fixed (AVFormatContext** context = &_context) ffmpeg.avformat_close_input(context);
        }
    }
}
