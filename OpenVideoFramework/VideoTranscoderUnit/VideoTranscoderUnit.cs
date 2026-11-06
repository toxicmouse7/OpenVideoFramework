using FFmpeg.AutoGen.Abstractions;
using Microsoft.Extensions.Logging;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.VideoTranscoderUnit;

public sealed class VideoTranscoderUnit : IPipelineUnit<VideoFrame, VideoFrame>, IDisposable
{
    private readonly VideoTranscoderUnitSettings _settings;
    private ILogger<VideoTranscoderUnit> _logger = null!;
    private Decoder? _decoder;
    private Encoder? _encoder;

    public VideoTranscoderUnit(VideoTranscoderUnitSettings settings) => _settings = settings;

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        _logger = context.GetLogger<VideoTranscoderUnit>();

        return Task.CompletedTask;
    }

    public async ValueTask ProcessAsync(VideoFrame frame, Emit<VideoFrame> emit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (frame.Codec == _settings.OutputCodec)
        {
            await emit(frame, cancellationToken);
            return;
        }

        _decoder ??= new Decoder(frame, frame.Codec, frame.Width, frame.Height, _settings.SpecificDecoder);
        var decodedFrames = _decoder.Decode(frame);
        if (decodedFrames.Length == 0)
        {
            return;
        }

        try
        {
            if (_encoder is null)
            {
                _encoder = CreateEncoder(frame, decodedFrames[0].PixelFormat);
                _logger.LogInformation(
                    "Initialized encoder and decoder. Encoder: {encoderName}. Decoder: {decoderName}.",
                    _encoder.Name,
                    _decoder.Name);
            }

            foreach (var raw in decodedFrames)
            {
                foreach (var encoded in _encoder.Encode(raw))
                {
                    await emit(encoded, cancellationToken);
                }
            }
        }
        finally
        {
            foreach (var raw in decodedFrames)
            {
                raw.Dispose();
            }
        }
    }

    private Encoder CreateEncoder(VideoFrame initFrame, AVPixelFormat pixelFormat)
    {
        return new Encoder(
            _settings.OutputCodec,
            initFrame.Width,
            initFrame.Height,
            pixelFormat,
            _settings.SpecificEncoder);
    }

    public void Dispose()
    {
        _decoder?.Dispose();
        _encoder?.Dispose();
    }
}
