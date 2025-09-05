using System.Threading.Channels;
using OpenVideoFramework.FrameAssemblerUnit.Jpeg;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtpDemuxUnit;

namespace OpenVideoFramework.FrameAssemblerUnit;

public class RtpFrameAssemblerUnit : IPipelineUnit<DemuxedData, CompleteFrame>
{
    private readonly Dictionary<uint, RtpFrameAssembler> _assemblers = new();

    public Task PrepareForExecutionAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ProcessAsync(
        ChannelReader<DemuxedData> reader,
        ChannelWriter<CompleteFrame> writer,
        CancellationToken cancellationToken)
    {
        await foreach (var demuxedData in reader.ReadAllAsync(cancellationToken))
        {
            if (!_assemblers.TryGetValue(demuxedData.Ssrc, out var assembler))
            {
                assembler = CreateAssemblerForCodec(demuxedData.StreamContext.Codec);
                _assemblers[demuxedData.Ssrc] = assembler;
            }
            
            var completeFrame = assembler.AddPacket(demuxedData);

            if (completeFrame is not null)
            {
                await writer.WriteAsync(completeFrame, cancellationToken);
            }
        }
    }

    private RtpFrameAssembler CreateAssemblerForCodec(Codec codec)
    {
        return codec switch
        {
            Codec.MJPEG => new JpegRtpFrameAssembler(),
            _ => throw new ArgumentOutOfRangeException(nameof(codec), codec, "Unsupported codec")
        };
    }
}