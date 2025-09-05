using System.Threading.Channels;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.RtspSource;

public class RtspSource : IPipelineSource<INetworkPacket>
{
    private readonly RtspSourceConfiguration _configuration;
    private readonly RtspClient _rtspClient;

    private ChannelWriter<INetworkPacket> _output = null!;

    public RtspSource(RtspSourceConfiguration configuration)
    {
        _configuration = configuration;
        _rtspClient = new RtspClient(configuration.Url);
    }
    
    public async Task PrepareForExecutionAsync(CancellationToken cancellationToken)
    {
        await _rtspClient.ConnectAsync(cancellationToken);
    }

    public async Task ProduceAsync(ChannelWriter<INetworkPacket> output, CancellationToken cancellationToken)
    {
        _output = output;

        await _rtspClient.ReceiveAsync(
            OnPacketReady,
            cancellationToken);
    }

    private async Task OnPacketReady(INetworkPacket packet)
    {
        await _output.WriteAsync(packet);
    }
}