using FFmpeg.AutoGen.Abstractions;
using Microsoft.Extensions.Logging;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.MediaFile;

/// <summary>Consumes paths to media files and produces encoded video access units.</summary>
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
        using var reader = new MediaReader(input);
        while (reader.ReadNext(cancellationToken) is { } frame)
        {
            await emit(frame, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Media file processed. Media file: {path}.", input);
    }

    private sealed unsafe class MediaReader : IDisposable
    {
        private AVFormatContext* _context;
        private AVPacket* _packet;
        private readonly Dictionary<int, FFmpegBitstreamFilter> _filters = [];
        private readonly Queue<EncodedVideoFrame> _pending = [];
        private bool _ended;

        public MediaReader(string path)
        {
            try
            {
                fixed (AVFormatContext** context = &_context)
                {
                    FFmpegPacketAdapter.Check(ffmpeg.avformat_open_input(context, path, null, null));
                }

                FFmpegPacketAdapter.Check(ffmpeg.avformat_find_stream_info(_context, null));
                _packet = ffmpeg.av_packet_alloc();
                if (_packet == null)
                {
                    throw new OutOfMemoryException();
                }

                for (var i = 0; i < _context->nb_streams; i++)
                {
                    var stream = _context->streams[i];
                    // This filter passes Annex B through and converts AVCC using stream extradata.
                    if (stream->codecpar->codec_id == AVCodecID.AV_CODEC_ID_H264)
                    {
                        _filters.Add(i, new FFmpegBitstreamFilter("h264_mp4toannexb", stream->codecpar, stream->time_base));
                    }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public EncodedVideoFrame? ReadNext(CancellationToken cancellationToken)
        {
            while (_pending.Count == 0 && !_ended)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = ffmpeg.av_read_frame(_context, _packet);
                if (result == ffmpeg.AVERROR_EOF)
                {
                    foreach (var (index, filter) in _filters)
                    {
                        filter.Send(null);
                        Receive(filter, _context->streams[index]);
                    }
                    _ended = true;
                    break;
                }
                FFmpegPacketAdapter.Check(result);
                try
                {
                    var stream = _context->streams[_packet->stream_index];
                    if (stream->codecpar->codec_type != AVMediaType.AVMEDIA_TYPE_VIDEO)
                    {
                        continue;
                    }

                    if (_filters.TryGetValue(_packet->stream_index, out var filter))
                    {
                        filter.Send(_packet);
                        Receive(filter, stream);
                    }
                    else
                    {
                        _pending.Enqueue(Convert(_packet, stream));
                    }
                }
                finally { ffmpeg.av_packet_unref(_packet); }
            }
            return _pending.TryDequeue(out var frame) ? frame : null;
        }

        private void Receive(FFmpegBitstreamFilter filter, AVStream* stream)
        {
            while (filter.Receive(_packet))
            {
                try
                { _pending.Enqueue(Convert(_packet, stream)); }
                finally { ffmpeg.av_packet_unref(_packet); }
            }
        }

        private static EncodedVideoFrame Convert(AVPacket* packet, AVStream* stream) =>
            FFmpegPacketAdapter.FromPacket(packet, FFmpegPacketAdapter.MapCodec(stream->codecpar->codec_id),
                stream->codecpar->width, stream->codecpar->height);

        public void Dispose()
        {
            foreach (var filter in _filters.Values)
            {
                filter.Dispose();
            }

            fixed (AVPacket** packet = &_packet)
            {
                ffmpeg.av_packet_free(packet);
            }

            fixed (AVFormatContext** context = &_context)
            {
                ffmpeg.avformat_close_input(context);
            }
        }
    }
}
