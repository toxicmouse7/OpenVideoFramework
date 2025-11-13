using System.Threading.Channels;
using FFmpeg.AutoGen.Abstractions;
using Microsoft.Extensions.Logging;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.VideoTranscoderUnit;

public sealed class VideoTranscoderUnit : IPipelineUnit<VideoFrame, VideoFrame>, IDisposable
{
    private readonly VideoTranscoderUnitSettings _settings;
    private ILogger<VideoTranscoderUnit> _logger = null!;
    private Decoder _decoder = null!;
    private Encoder _encoder = null!;

    public VideoTranscoderUnit(VideoTranscoderUnitSettings settings)
    {
        _settings = settings;
    }

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        _logger = context.GetLogger<VideoTranscoderUnit>();

        return Task.CompletedTask;
    }

    public async Task ProcessAsync(
        ChannelReader<VideoFrame> reader,
        ChannelWriter<VideoFrame> writer,
        CancellationToken cancellationToken)
    {
        await RuntimeInitializeAsync(reader, writer, cancellationToken);
        
        await foreach (var frame in reader.ReadAllAsync(cancellationToken))
        {
            if (frame.Codec == _settings.OutputCodec)
            {
                await writer.WriteAsync(frame, cancellationToken);
                continue;
            }
            
            var transcodedFrames = TranscodeFrame(frame);
            foreach (var transcodedFrame in transcodedFrames)
            {
                await writer.WriteAsync(transcodedFrame, cancellationToken);
            }
        }
    }

    private async Task RuntimeInitializeAsync(
        ChannelReader<VideoFrame> reader,
        ChannelWriter<VideoFrame> writer,
        CancellationToken cancellationToken)
    {
        var initFrame = await reader.ReadAsync(cancellationToken);
        if (initFrame.Codec == _settings.OutputCodec)
        {
            _logger.LogInformation("Input and output codecs are equal. Nothing will be transcoded.");
            return;
        }
        
        _decoder = new Decoder(
            initFrame,
            initFrame.Codec,
            initFrame.Width,
            initFrame.Height,
            _settings.SpecificDecoder);

        var decodedFrames = _decoder.Decode(initFrame);

        if (decodedFrames.Length > 0)
        {
            CreateEncoder(initFrame, decodedFrames.First().PixelFormat);
            
            foreach (var transcoded in EncodeDecodedFrames(decodedFrames))
            {
                await writer.WriteAsync(transcoded, cancellationToken);
            }
            return;
        }
        
        await foreach (var frame in reader.ReadAllAsync(cancellationToken))
        {
            decodedFrames = _decoder.Decode(frame);

            if (decodedFrames.Length == 0) continue;

            CreateEncoder(frame, decodedFrames.First().PixelFormat);
            
            foreach (var transcoded in EncodeDecodedFrames(decodedFrames))
            {
                await writer.WriteAsync(transcoded, cancellationToken);
            }

            break;
        }

        _logger.LogInformation(
            "Initialized encoder and decoder. Encoder: {encoderName}. Decoder: {decoderName}.",
            _encoder.Name,
            _decoder.Name);
    }
    
    private void CreateEncoder(VideoFrame initFrame, AVPixelFormat pixelFormat)
    {
        _encoder = new Encoder(
            _settings.OutputCodec,
            initFrame.Width,
            initFrame.Height,
            pixelFormat,
            _settings.SpecificEncoder);
    }

    private IEnumerable<VideoFrame> TranscodeFrame(VideoFrame frame)
    {
        var rawFrames = _decoder.Decode(frame);
        return EncodeDecodedFrames(rawFrames);
    }

    private IEnumerable<VideoFrame> EncodeDecodedFrames(RawFrame[] rawFrames)
    {
        foreach (var raw in rawFrames)
        {
            foreach (var encoded in _encoder.Encode(raw))
                yield return encoded;

            raw.Dispose();
        }
    }

    public void Dispose()
    {
        _decoder.Dispose();
        _encoder.Dispose();
    }
}