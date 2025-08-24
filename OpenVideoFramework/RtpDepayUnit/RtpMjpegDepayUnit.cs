using System.Threading.Channels;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtpDepayUnit;

public class RtpMjpegDepayUnit : IPipelineUnit<RtpPacket, byte[]>
{
    public Task PrepareForExecutionAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task ProcessAsync(ChannelReader<RtpPacket> reader, ChannelWriter<byte[]> writer, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}