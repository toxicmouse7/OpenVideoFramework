using System.Threading.Channels;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers;
using OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers.AC3;
using OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers.Jpeg;
using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtpFrameAssemblerUnit;

public class RtpFrameAssemblerUnit : IPipelineUnit<RtpPacket, CompleteFrame>
{
    private readonly Dictionary<uint, RtpFrameAssembler> _assemblers = new();
    private PipelineContext _context = null!;

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        _context = context;
        return Task.CompletedTask;
    }

    public async Task ProcessAsync(
        ChannelReader<RtpPacket> reader,
        ChannelWriter<CompleteFrame> writer,
        CancellationToken cancellationToken)
    {
        await foreach (var packet in reader.ReadAllAsync(cancellationToken))
        {
            if (!_assemblers.TryGetValue(packet.Header.SSRC, out var assembler))
            {
                assembler = CreateAssembler(packet.Header.PayloadType);
                _assemblers.Add(packet.Header.SSRC, assembler);
            }
            
            var frame = assembler.AddPacket(packet);

            if (frame is not null)
            {
                await writer.WriteAsync(frame, cancellationToken);
            }
        }
    }

    private RtpFrameAssembler CreateAssembler(PayloadType payloadType)
    {
        return payloadType switch
        {
            PayloadType.JPEG => new JpegRtpFrameAssembler(_context.GetLogger<JpegRtpFrameAssembler>()),
            PayloadType.AC3 => new AC3RtpFrameAssembler(_context.GetLogger<AC3RtpFrameAssembler>()),
            _ => throw new NotSupportedException($"Payload type {payloadType} is not supported.")
        };
    }
}