using System.Net;
using System.Net.Sockets;

namespace OpenVideoFramework.RtspSource.Rtcp;

public sealed class RtcpClient : IDisposable
{
    private readonly UdpClient _rtcpClient;

    public RtcpClient()
    {
        _rtcpClient = new UdpClient(0);
    }

    public int Port => ((IPEndPoint)_rtcpClient.Client.LocalEndPoint!).Port;

    public void Connect(IPEndPoint endPoint)
    {
        _rtcpClient.Connect(endPoint);
    }

    public async Task SendReceiverReportAsync(RtcpReceiverReport receiverReport)
    {
        await _rtcpClient.SendAsync(RtcpSerializer.SerializeReceiverReport(receiverReport));
    }

    public async Task<RtcpPacket> GetPacketAsync(CancellationToken token)
    {
        var udpPacket = await _rtcpClient.ReceiveAsync(token);

        var rtcpPacket = RtcpSerializer.DeserializeRtcpPacket(udpPacket.Buffer);

        return rtcpPacket;
    }

    public void Dispose()
    {
        _rtcpClient.Dispose();
    }
}