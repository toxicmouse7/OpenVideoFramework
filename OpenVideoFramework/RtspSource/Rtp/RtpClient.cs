using System.Net;
using System.Net.Sockets;

namespace OpenVideoFramework.RtspSource.Rtp;

public sealed class RtpClient : IDisposable
{
    private readonly UdpClient _rtpClient;

    public RtpClient()
    {
        _rtpClient = new UdpClient(0);
    }

    public async Task<RtpPacket> GetPacketAsync(CancellationToken token = default)
    {
        var udpPacket = await _rtpClient.ReceiveAsync(token);
        
        var rtpPacket = RtpSerializer.DeserializeRtpPacket(udpPacket.Buffer);

        return rtpPacket;
    }
    
    public int Port => ((IPEndPoint)_rtpClient.Client.LocalEndPoint!).Port;

    public void Dispose()
    {
        _rtpClient.Dispose();
    }
}