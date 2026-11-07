using FFmpeg.AutoGen.Abstractions;

namespace OpenVideoFramework.Common;

internal sealed unsafe class FFmpegBitstreamFilter : IDisposable
{
    private AVBSFContext* _context;
    internal AVCodecParameters* Parameters => _context->par_out;

    internal FFmpegBitstreamFilter(string name, AVCodecParameters* parameters, AVRational timeBase)
    {
        var filter = ffmpeg.av_bsf_get_by_name(name);
        if (filter == null)
        {
            throw new NotSupportedException($"FFmpeg bitstream filter {name} is unavailable.");
        }

        try
        {
            fixed (AVBSFContext** context = &_context)
            {
                FFmpegPacketAdapter.Check(ffmpeg.av_bsf_alloc(filter, context));
            }

            FFmpegPacketAdapter.Check(ffmpeg.avcodec_parameters_copy(_context->par_in, parameters));
            _context->time_base_in = timeBase;
            FFmpegPacketAdapter.Check(ffmpeg.av_bsf_init(_context));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    // Successful sends transfer the packet reference to FFmpeg; null starts draining.
    internal void Send(AVPacket* packet) => FFmpegPacketAdapter.Check(ffmpeg.av_bsf_send_packet(_context, packet));

    internal bool Receive(AVPacket* packet)
    {
        var result = ffmpeg.av_bsf_receive_packet(_context, packet);
        if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN) || result == ffmpeg.AVERROR_EOF)
        {
            return false;
        }

        FFmpegPacketAdapter.Check(result);
        return true;
    }

    public void Dispose()
    {
        fixed (AVBSFContext** context = &_context)
        {
            ffmpeg.av_bsf_free(context);
        }
    }
}
