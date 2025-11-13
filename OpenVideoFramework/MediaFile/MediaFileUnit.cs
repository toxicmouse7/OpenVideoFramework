using System.Threading.Channels;
using FFmpeg.AutoGen.Abstractions;
using Microsoft.Extensions.Logging;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.MediaFile;

/// <summary>
/// Consumes paths to media files. Loads them and produces <see cref="CompleteFrame"/>.
/// </summary>
public class MediaFileUnit : IPipelineUnit<string, CompleteFrame>
{
    private ILogger<MediaFileUnit> _logger = null!;
    
    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        _logger = context.GetLogger<MediaFileUnit>();
        
        return Task.CompletedTask;
    }

    public async Task ProcessAsync(ChannelReader<string> reader, ChannelWriter<CompleteFrame> writer,
        CancellationToken cancellationToken)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                ProcessMediaFile(item, writer, cancellationToken);
                _logger.LogInformation("Media file processed. Media file: {path}.", item);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error occured while processing media file. Media file: {path}.", item);
            }
        }
    }

    private unsafe void ProcessMediaFile(
        string mediaPath,
        ChannelWriter<CompleteFrame> writer,
        CancellationToken cancellationToken)
    {
        AVFormatContext* formatContext = null;

        ffmpeg.avformat_open_input(&formatContext, mediaPath, null, null);
        ffmpeg.avformat_find_stream_info(formatContext, null);

        var packet = ffmpeg.av_packet_alloc();

        while (ffmpeg.av_read_frame(formatContext, packet) >= 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var packetStream = formatContext->streams[packet->stream_index];
            if (packetStream->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                var frame = Utils.AVPacketToVideoFrame(
                    packet, packetStream->codecpar, (uint)packetStream->time_base.den,
                    Utils.MapCodec(packetStream->codecpar->codec_id),
                    packetStream->codecpar->width, packetStream->codecpar->height,
                    DateTimeOffset.Now, TimeSpan.FromSeconds(packet->duration * ffmpeg.av_q2d(packetStream->time_base)));

                while (!writer.TryWrite(frame))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }

            ffmpeg.av_packet_unref(packet);
        }
        
        ffmpeg.av_packet_free(&packet);
        ffmpeg.avformat_close_input(&formatContext);
    }
}