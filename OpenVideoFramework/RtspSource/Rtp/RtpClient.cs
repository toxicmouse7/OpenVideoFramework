using System.Net;
using System.Net.Sockets;
using OpenVideoFramework.RtspSource.SDP;

namespace OpenVideoFramework.RtspSource.Rtp;

internal sealed class RtpClient : IDisposable
{
    private readonly UdpClient _rtpClient;
    private readonly TrackMetadata _trackMetadata;

    public RtpClient(TrackMetadata trackMetadata)
    {
        _trackMetadata = trackMetadata;
        _rtpClient = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
    }
    
    public void Connect(IPEndPoint endPoint)
    { 
        _rtpClient.Connect(endPoint);
    }

    public async Task<RtpPacket> GetPacketAsync(CancellationToken token = default)
    {
        var udpPacket = await _rtpClient.ReceiveAsync(token);
        
        var rtpPacket = RtpSerializer.DeserializeRtpPacket(
            udpPacket.Buffer,
            _trackMetadata.PayloadType,
            _trackMetadata.ClockRate);

        return rtpPacket;
    }
    
    public int Port => ((IPEndPoint)_rtpClient.Client.LocalEndPoint!).Port;

    public void Dispose()
    {
        _rtpClient.Dispose();
    }
}