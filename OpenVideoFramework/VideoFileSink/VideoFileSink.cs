using FFmpeg.AutoGen.Abstractions;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.VideoFileSink;

/// <summary>Writes constant-frame-rate video, rotating at keyframes. Reordered H.264 is unsupported.</summary>
public class VideoFileSink : IPipelineSink<EncodedVideoFrame>, IDisposable
{
    private readonly VideoFileSinkSettings _settings;
    private readonly AVRational _frameTimeBase;
    private unsafe AVFormatContext* _formatContext;
    private unsafe AVStream* _stream;
    private unsafe AVCodecParameters* _parameters;
    private unsafe AVCodecParserContext* _parser;
    private unsafe AVCodecContext* _parserContext;
    private long _frameCount;
    private int? _lastPictureNumber;
    private bool _headerWritten;
    private bool _disposed;

    public VideoFileSink(VideoFileSinkSettings settings)
    {
        if (!double.IsFinite(settings.Fps) || settings.Fps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.Fps));
        }

        _settings = settings;
        _frameTimeBase = ffmpeg.av_inv_q(ffmpeg.av_d2q(settings.Fps, 1000000));
        if (_frameTimeBase.num <= 0 || _frameTimeBase.den <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.Fps));
        }
    }

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask ConsumeAsync(EncodedVideoFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(frame);
        return ValueTask.CompletedTask;
    }

    private unsafe void Write(EncodedVideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_parameters == null && frame.Codec == Codec.H264 && !frame.IsKeyFrame)
        {
            return;
        }

        var packet = FFmpegPacketAdapter.ToPacket(frame);
        try
        {
            if (_parameters == null)
            {
                Initialize(frame, packet);
            }

            if (_parameters->codec_id != FFmpegPacketAdapter.MapCodec(frame.Codec) ||
                _parameters->width != frame.Width || _parameters->height != frame.Height)
            {
                throw new NotSupportedException("Video codec and dimensions must remain constant.");
            }

            if (frame.Codec == Codec.H264)
            {
                ValidateH264(packet);
                if (_formatContext == null && (packet->flags & ffmpeg.AV_PKT_FLAG_KEY) == 0)
                {
                    throw new NotSupportedException("An H.264 segment must begin with a keyframe.");
                }
            }
            if (_formatContext == null ||
                (_frameCount * ffmpeg.av_q2d(_frameTimeBase) >= _settings.RollPeriod.TotalSeconds &&
                 (frame.Codec != Codec.H264 || (packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0)))
            {
                RollFile();
            }

            packet->stream_index = _stream->index;
            packet->pts = packet->dts = ffmpeg.av_rescale_q(_frameCount, _frameTimeBase, _stream->time_base);
            var next = ffmpeg.av_rescale_q(_frameCount + 1, _frameTimeBase, _stream->time_base);
            packet->duration = next - packet->pts;
            if (packet->duration <= 0)
            {
                throw new NotSupportedException("The container time base cannot represent the configured FPS.");
            }

            FFmpegPacketAdapter.Check(ffmpeg.av_interleaved_write_frame(_formatContext, packet));
            _frameCount++;
        }
        catch { Dispose(); throw; }
        finally { ffmpeg.av_packet_free(&packet); }
    }

    private unsafe void Initialize(EncodedVideoFrame frame, AVPacket* packet)
    {
        _parameters = ffmpeg.avcodec_parameters_alloc();
        if (_parameters == null)
        {
            throw new OutOfMemoryException();
        }

        _parameters->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        _parameters->codec_id = FFmpegPacketAdapter.MapCodec(frame.Codec);
        _parameters->width = frame.Width;
        _parameters->height = frame.Height;
        if (frame.Codec != Codec.H264)
        {
            return;
        }

        _parser = ffmpeg.av_parser_init(AVCodecID.AV_CODEC_ID_H264);
        _parserContext = ffmpeg.avcodec_alloc_context3(null);
        if (_parser == null || _parserContext == null)
        {
            throw new OutOfMemoryException();
        }

        _parser->flags |= ffmpeg.PARSER_FLAG_COMPLETE_FRAMES;
        _parserContext->codec_id = AVCodecID.AV_CODEC_ID_H264;

        // Extract parameter sets once. MOV/MP4 accepts Annex B extradata and converts
        // both configuration and packet NAL units to AVCC itself.
        using var filter = new FFmpegBitstreamFilter("extract_extradata", _parameters, _frameTimeBase);
        var copy = ffmpeg.av_packet_clone(packet);
        var output = ffmpeg.av_packet_alloc();
        if (copy == null || output == null)
        {
            ffmpeg.av_packet_free(&copy);
            ffmpeg.av_packet_free(&output);
            throw new OutOfMemoryException();
        }
        try
        {
            filter.Send(copy);
            var flushing = false;
            while (true)
            {
                if (!filter.Receive(output))
                {
                    if (flushing)
                    {
                        break;
                    }

                    filter.Send(null);
                    flushing = true;
                    continue;
                }
                ulong size = 0;
                var data = ffmpeg.av_packet_get_side_data(output, AVPacketSideDataType.AV_PKT_DATA_NEW_EXTRADATA, &size);
                if (data != null && size > 0 && _parameters->extradata == null)
                {
                    _parameters->extradata = (byte*)ffmpeg.av_mallocz(size + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE);
                    if (_parameters->extradata == null)
                    {
                        throw new OutOfMemoryException();
                    }

                    _parameters->extradata_size = checked((int)size);
                    new ReadOnlySpan<byte>(data, (int)size).CopyTo(new Span<byte>(_parameters->extradata, (int)size));
                }
                ffmpeg.av_packet_unref(output);
            }
            if (_parameters->extradata == null)
            {
                throw new NotSupportedException("H.264 recording requires in-band SPS/PPS at the first keyframe.");
            }
        }
        finally
        {
            ffmpeg.av_packet_free(&copy);
            ffmpeg.av_packet_free(&output);
        }
    }

    private unsafe void ValidateH264(AVPacket* packet)
    {
        byte* output;
        var size = 0;
        _parser->pict_type = (int)AVPictureType.AV_PICTURE_TYPE_NONE;
        var consumed = ffmpeg.av_parser_parse2(_parser, _parserContext, &output, &size,
            packet->data, packet->size, ffmpeg.AV_NOPTS_VALUE, ffmpeg.AV_NOPTS_VALUE, 0);
        FFmpegPacketAdapter.Check(consumed);
        if (consumed != packet->size || size != packet->size ||
            _parser->pict_type is not ((int)AVPictureType.AV_PICTURE_TYPE_I) and not ((int)AVPictureType.AV_PICTURE_TYPE_P) ||
            _parser->picture_structure != AVPictureStructure.AV_PICTURE_STRUCTURE_FRAME ||
            _parser->field_order != AVFieldOrder.AV_FIELD_PROGRESSIVE ||
            (_lastPictureNumber.HasValue &&
             !(_parser->key_frame == 1 && _parser->output_picture_number == 0) &&
             _parser->output_picture_number <= _lastPictureNumber.Value))
        {
            throw new NotSupportedException("H.264 recording supports progressive I/P pictures in presentation order only; transcode reordered input first.");
        }

        if (_parser->width != _parameters->width || _parser->height != _parameters->height)
        {
            throw new NotSupportedException("H.264 dimensions must remain constant.");
        }

        _lastPictureNumber = _parser->output_picture_number;
        // Restrict segment starts to keyframes that reset picture order.
        if (_parser->key_frame != 1 || _parser->output_picture_number != 0)
        {
            packet->flags &= ~ffmpeg.AV_PKT_FLAG_KEY;
        }
    }

    private unsafe void RollFile()
    {
        CloseFile();
        var extension = Path.GetExtension(_settings.OutputPath);
        var stem = $"{Path.ChangeExtension(_settings.OutputPath, null)} {DateTime.Now:dd-MM-yy}";
        var filename = stem + extension;
        for (var i = 1; File.Exists(filename); i++)
        {
            filename = $"{stem} ({i}){extension}";
        }

        try
        {
            fixed (AVFormatContext** context = &_formatContext)
            {
                FFmpegPacketAdapter.Check(ffmpeg.avformat_alloc_output_context2(context, null, null, filename));
            }

            if (_formatContext == null)
            {
                throw new OutOfMemoryException();
            }

            _stream = ffmpeg.avformat_new_stream(_formatContext, null);
            if (_stream == null)
            {
                throw new OutOfMemoryException();
            }

            FFmpegPacketAdapter.Check(ffmpeg.avcodec_parameters_copy(_stream->codecpar, _parameters));
            _stream->time_base = _frameTimeBase;
            _stream->avg_frame_rate = ffmpeg.av_inv_q(_frameTimeBase);
            if ((_formatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                FFmpegPacketAdapter.Check(ffmpeg.avio_open(&_formatContext->pb, filename, ffmpeg.AVIO_FLAG_WRITE));
            }

            FFmpegPacketAdapter.Check(ffmpeg.avformat_write_header(_formatContext, null));
            _headerWritten = true;
            _frameCount = 0;
        }
        catch { CloseFile(); throw; }
    }

    private unsafe void CloseFile()
    {
        if (_formatContext == null)
        {
            return;
        }

        var trailerResult = 0;
        var closeResult = 0;
        try
        {
            if (_headerWritten)
            {
                trailerResult = ffmpeg.av_write_trailer(_formatContext);
            }
        }
        finally
        {
            if (_formatContext->pb != null && (_formatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                closeResult = ffmpeg.avio_closep(&_formatContext->pb);
            }

            ffmpeg.avformat_free_context(_formatContext);
            _formatContext = null;
            _stream = null;
            _headerWritten = false;
        }
        FFmpegPacketAdapter.Check(trailerResult);
        FFmpegPacketAdapter.Check(closeResult);
    }

    public ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        { CloseFile(); }
        finally
        {
            if (_parser != null)
            {
                ffmpeg.av_parser_close(_parser);
            }

            _parser = null;
            fixed (AVCodecContext** context = &_parserContext)
            {
                ffmpeg.avcodec_free_context(context);
            }

            fixed (AVCodecParameters** parameters = &_parameters)
            {
                ffmpeg.avcodec_parameters_free(parameters);
            }
        }
    }
}
