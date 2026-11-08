using System.Net;
using System.Net.Sockets;
using OpenVideoFramework.RtspSource.SDP;

namespace OpenVideoFramework.RtspSource.Rtp;

internal sealed class RtpClient : IDisposable
{
    private readonly UdpClient _rtpClient;
    private readonly TrackMetadata _trackMetadata;

    public RtpClient(TrackMetadata trackMetadata, AddressFamily addressFamily)
    {
        _trackMetadata = trackMetadata;
        var bindAddress = addressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
        _rtpClient = new UdpClient(new IPEndPoint(bindAddress, 0));
    }

    public void Connect(IPEndPoint endPoint) => _rtpClient.Connect(endPoint);

    public async Task<RtpPacket> GetPacketAsync(CancellationToken token = default)
    {
        var udpPacket = await _rtpClient.ReceiveAsync(token);

        var rtpPacket = RtpSerializer.DeserializeRtpPacket(
            udpPacket.Buffer,
            _trackMetadata.PayloadType);

        return rtpPacket;
    }

    public int Port => ((IPEndPoint)_rtpClient.Client.LocalEndPoint!).Port;

    public void Dispose() => _rtpClient.Dispose();
}
