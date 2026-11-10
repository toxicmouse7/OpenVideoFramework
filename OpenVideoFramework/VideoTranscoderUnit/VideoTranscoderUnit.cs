using Microsoft.Extensions.Logging;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.VideoTranscoderUnit;

public sealed class VideoTranscoderUnit :
    IPipelineUnit<MediaSample<EncodedVideoFrame>, MediaSample<EncodedVideoFrame>>, IDisposable
{
    private readonly VideoTranscoderUnitSettings _settings;
    private ILogger<VideoTranscoderUnit> _logger = null!;
    private Decoder? _decoder;
    private Encoder? _encoder;
    private int? _trackId;

    public VideoTranscoderUnit(VideoTranscoderUnitSettings settings)
    {
        if (!double.IsFinite(settings.Fps) || settings.Fps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.Fps));
        }

        _settings = settings;
    }

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        _logger = context.GetLogger<VideoTranscoderUnit>();
        return Task.CompletedTask;
    }

    public async ValueTask ProcessAsync(
        MediaSample<EncodedVideoFrame> sample,
        Emit<MediaSample<EncodedVideoFrame>> emit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_trackId.HasValue && _trackId != sample.TrackId)
        {
            throw new NotSupportedException("A video transcoder processes one track. Use a separate unit per track.");
        }

        _trackId = sample.TrackId;
        _decoder ??= new Decoder(sample.Frame, _settings.SpecificDecoder);
        await EncodeFrames(_decoder.Decode(sample), emit, cancellationToken);
    }

    private async ValueTask EncodeFrames(
        List<RawFrame> frames, Emit<MediaSample<EncodedVideoFrame>> emit, CancellationToken token)
    {
        try
        {
            foreach (var raw in frames)
            {
                if (_encoder is null)
                {
                    _encoder = new Encoder(_settings, raw, _trackId!.Value);
                    _logger.LogInformation("Initialized encoder {encoder} and decoder {decoder}.", _encoder.Name, _decoder!.Name);
                }
                foreach (var encoded in _encoder.Encode(raw))
                {
                    await emit(encoded, token);
                }
            }
        }
        finally
        {
            foreach (var raw in frames)
            {
                raw.Dispose();
            }
        }
    }

    public async ValueTask CompleteAsync(
        Emit<MediaSample<EncodedVideoFrame>> emit, CancellationToken cancellationToken)
    {
        if (_decoder is null)
        {
            return;
        }

        try
        {
            await EncodeFrames(_decoder.Decode(null), emit, cancellationToken);
            if (_encoder is not null)
            {
                foreach (var frame in _encoder.Encode(null))
                {
                    await emit(frame, cancellationToken);
                }
            }
        }
        finally { Dispose(); }
    }

    public void Dispose()
    {
        _decoder?.Dispose();
        _encoder?.Dispose();
        _decoder = null;
        _encoder = null;
    }
}
