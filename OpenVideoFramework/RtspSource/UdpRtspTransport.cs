using System.Net;
using System.Net.Sockets;
using OpenVideoFramework.RtspSource.Rtcp;
using OpenVideoFramework.RtspSource.SDP;

namespace OpenVideoFramework.RtspSource;

internal sealed class UdpRtspTransport : RtspTransport
{
    private readonly Dictionary<TrackReceiver, (UdpClient Rtp, UdpClient Rtcp)> _tracks = [];

    public override TrackReceiver CreateTrack(TrackMetadata metadata)
    {
        var track = new TrackReceiver(metadata);
        var bindAddress = ServerAddress.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
        var rtp = new UdpClient(new IPEndPoint(bindAddress, 0));
        try
        {
            var rtcp = new UdpClient(new IPEndPoint(bindAddress, 0));
            _tracks.Add(track, (rtp, rtcp));
        }
        catch { rtp.Dispose(); throw; }
        return track;
    }

    public override string GetTransportHeader(TrackReceiver track)
    {
        var clients = _tracks[track];
        var rtpPort = ((IPEndPoint)clients.Rtp.Client.LocalEndPoint!).Port;
        var rtcpPort = ((IPEndPoint)clients.Rtcp.Client.LocalEndPoint!).Port;
        return $"Transport: RTP/AVP/UDP;unicast;client_port={rtpPort}-{rtcpPort}";
    }

    public override void ConfigureTrack(TrackReceiver track, string transport)
    {
        var protocol = transport.Split(';')[0].Trim();
        if (!protocol.Equals("RTP/AVP", StringComparison.OrdinalIgnoreCase) &&
            !protocol.Equals("RTP/AVP/UDP", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The server did not accept UDP transport.");
        }

        var ports = GetTransportParameter(transport, "server_port").Split('-');
        if (ports.Length != 2 || !int.TryParse(ports[0], out var rtp) || !int.TryParse(ports[1], out var rtcp) ||
            rtp is < 1 or > 65535 || rtcp is < 1 or > 65535)
        {
            throw new InvalidDataException("Invalid UDP server ports in RTSP Transport header.");
        }

        var clients = _tracks[track];
        clients.Rtp.Connect(new IPEndPoint(ServerAddress, rtp));
        clients.Rtcp.Connect(new IPEndPoint(ServerAddress, rtcp));
    }

    protected override IEnumerable<Task> ReceiveMedia(CancellationToken token)
    {
        foreach (var (track, clients) in _tracks)
        {
            yield return ReceiveRtpAsync(track, clients.Rtp, token);
            yield return ReceiveRtcpAsync(clients.Rtcp, token);
        }
    }

    private async Task ReceiveRtpAsync(TrackReceiver track, UdpClient client, CancellationToken token)
    {
        while (true)
        {
            var packet = await client.ReceiveAsync(token).ConfigureAwait(false);
            await ReceiveRtpAsync(track, packet.Buffer, token).ConfigureAwait(false);
        }
    }

    private async Task ReceiveRtcpAsync(UdpClient client, CancellationToken token)
    {
        while (true)
        {
            var packet = await client.ReceiveAsync(token).ConfigureAwait(false);
            ReceiveRtcp(packet.Buffer);
        }
    }

    public override async ValueTask SendReceiverReportAsync(TrackReceiver track, RtcpReceiverReport report, CancellationToken token) =>
        await _tracks[track].Rtcp.SendAsync(RtcpSerializer.SerializeReceiverReport(report), token).ConfigureAwait(false);

    public override void Dispose()
    {
        base.Dispose();
        foreach (var clients in _tracks.Values)
        {
            clients.Rtp.Dispose();
            clients.Rtcp.Dispose();
        }
    }
}
