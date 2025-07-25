using System.Threading.Channels;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtspSource;

public class RtspSource : IPipelineSource<RtpPacket>
{
    private readonly RtspSourceConfiguration _configuration;
    private readonly RtspClient _rtspClient;

    private ChannelWriter<RtpPacket> _output = null!;

    public RtspSource(RtspSourceConfiguration configuration)
    {
        _configuration = configuration;
        _rtspClient = new RtspClient(configuration.Url);
    }
    
    public async Task PrepareForExecutionAsync(CancellationToken cancellationToken)
    {
        await _rtspClient.ConnectAsync(cancellationToken);
    }

    public async Task ProduceAsync(ChannelWriter<RtpPacket> output, CancellationToken cancellationToken)
    {
        _output = output;

        await _rtspClient.ReceiveAsync(
            OnRtpPacketReady,
            cancellationToken);
    }

    private async Task OnRtpPacketReady(RtpPacket packet)
    {
        await _output.WriteAsync(packet);
    }
}