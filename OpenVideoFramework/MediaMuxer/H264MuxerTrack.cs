using FFmpeg.AutoGen.Abstractions;
using OpenVideoFramework.Common;

namespace OpenVideoFramework.MediaMuxer;

internal sealed unsafe class H264MuxerTrack : IDisposable
{
    private const int MaxConfigurationBytes = 1024 * 1024;
    private readonly List<byte[]> _parameterSets = [];
    private AVCodecParserContext* _parser;
    private AVCodecContext* _context;
    private int _width;
    private int _height;
    private int _configurationBytes;
    private bool _hasSps;
    private bool _hasPps;
    private bool _configured;

    public bool HasConfiguration => _hasSps && _hasPps;

    public (bool HasPicture, bool IsIdr) Inspect(ReadOnlySpan<byte> data)
    {
        var (start, prefix) = FindStartCode(data, 0);
        if (start < 0 || data[..start].IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new ArgumentException("H.264 data must use Annex B start codes.");
        }

        var hasPicture = false;
        var isIdr = false;
        while (start >= 0)
        {
            var nalStart = start + prefix;
            var (next, nextPrefix) = FindStartCode(data, nalStart);
            var end = next >= 0 ? next : data.Length;
            if (end > nalStart)
            {
                var nal = data[nalStart..end];
                var type = nal[0] & 0x1F;
                hasPicture |= type is >= 1 and <= 5;
                isIdr |= type == 5;
                if (type is 7 or 8)
                {
                    AddParameterSet(nal);
                }
            }

            start = next;
            prefix = nextPrefix;
        }

        return (hasPicture, isIdr);
    }

    private static (int Start, int Length) FindStartCode(ReadOnlySpan<byte> data, int offset)
    {
        for (var index = offset; index + 2 < data.Length; index++)
        {
            if (data[index] != 0 || data[index + 1] != 0)
            {
                continue;
            }

            if (data[index + 2] == 1)
            {
                return (index, 3);
            }

            if (index + 3 < data.Length && data[index + 2] == 0 && data[index + 3] == 1)
            {
                return (index, 4);
            }
        }

        return (-1, 0);
    }

    private void AddParameterSet(ReadOnlySpan<byte> nal)
    {
        // Annex B permits trailing_zero_8bits outside the NAL payload.
        nal = nal.TrimEnd((byte)0);
        foreach (var existing in _parameterSets)
        {
            if (nal.SequenceEqual(existing))
            {
                return;
            }
        }

        if (_configured)
        {
            throw new NotSupportedException("Changing H.264 parameter sets during recording is unsupported.");
        }

        _configurationBytes = checked(_configurationBytes + nal.Length + 4);
        if (_configurationBytes > MaxConfigurationBytes)
        {
            throw new NotSupportedException("H.264 configuration exceeds 1 MiB.");
        }

        _parameterSets.Add(nal.ToArray());
        _hasSps |= (nal[0] & 0x1F) == 7;
        _hasPps |= (nal[0] & 0x1F) == 8;
    }

    public void Configure(AVCodecParameters* parameters, AVPacket* firstIdr)
    {
        _width = parameters->width;
        _height = parameters->height;
        parameters->extradata = (byte*)ffmpeg.av_mallocz(
            (ulong)(_configurationBytes + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
        if (parameters->extradata == null)
        {
            throw new OutOfMemoryException();
        }

        parameters->extradata_size = _configurationBytes;
        var destination = new Span<byte>(parameters->extradata, _configurationBytes);
        foreach (var nal in _parameterSets)
        {
            destination[3] = 1;
            nal.CopyTo(destination[4..]);
            destination = destination[(nal.Length + 4)..];
        }

        _parser = ffmpeg.av_parser_init(AVCodecID.AV_CODEC_ID_H264);
        _context = ffmpeg.avcodec_alloc_context3(null);
        if (_parser == null || _context == null)
        {
            throw new OutOfMemoryException();
        }

        _parser->flags |= ffmpeg.PARSER_FLAG_COMPLETE_FRAMES;
        FFmpegPacketAdapter.Check(ffmpeg.avcodec_parameters_to_context(_context, parameters));
        Validate(firstIdr, true);
        parameters->profile = _context->profile;
        parameters->level = _context->level;
        _configured = true;
    }

    public void Validate(AVPacket* packet, bool requireIdr)
    {
        byte* output;
        var size = 0;
        _parser->pict_type = (int)AVPictureType.AV_PICTURE_TYPE_NONE;
        _parser->width = _parser->height = 0;
        var consumed = ffmpeg.av_parser_parse2(_parser, _context, &output, &size,
            packet->data, packet->size, ffmpeg.AV_NOPTS_VALUE, ffmpeg.AV_NOPTS_VALUE, 0);
        FFmpegPacketAdapter.Check(consumed);
        if (consumed != packet->size || size != packet->size ||
            _parser->width != _width || _parser->height != _height ||
            _parser->pict_type == (int)AVPictureType.AV_PICTURE_TYPE_NONE ||
            (requireIdr && _parser->key_frame != 1))
        {
            throw new NotSupportedException("H.264 picture or codec configuration is invalid or incompatible.");
        }
    }

    public void Dispose()
    {
        _parameterSets.Clear();
        if (_parser != null)
        {
            ffmpeg.av_parser_close(_parser);
            _parser = null;
        }

        if (_context != null)
        {
            fixed (AVCodecContext** context = &_context)
            {
                ffmpeg.avcodec_free_context(context);
            }
        }
    }
}
