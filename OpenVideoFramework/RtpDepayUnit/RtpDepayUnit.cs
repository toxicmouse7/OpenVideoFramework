using System.Threading.Channels;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtpDepayUnit.Rtp;
using OpenVideoFramework.RtspSource;
using RtpPacket = OpenVideoFramework.RtpDepayUnit.Rtp.RtpPacket;

namespace OpenVideoFramework.RtpDepayUnit;

public class RtpDepayUnit : IPipelineUnit<INetworkPacket, RtpPacket>
{
    public Task PrepareForExecutionAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ProcessAsync(
        ChannelReader<INetworkPacket> reader,
        ChannelWriter<RtpPacket> writer,
        CancellationToken cancellationToken)
    {
        await foreach (var networkPacket in reader.ReadAllAsync(cancellationToken))
        {
            if (networkPacket.Protocol != ProtocolType.Rtp)
            {
                continue;
            }

            var rtpHeader = RtpPacketHeader.Deserialize(networkPacket.Data);
            
            var rtpPacket = new RtpPacket
            {
                Header = rtpHeader,
                Content = networkPacket.Data[rtpHeader.Size..],
            };
            
            await writer.WriteAsync(rtpPacket, cancellationToken);
        }
    }
}