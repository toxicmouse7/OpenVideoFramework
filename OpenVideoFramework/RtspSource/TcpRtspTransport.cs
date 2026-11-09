using System.Buffers.Binary;
using OpenVideoFramework.RtspSource.Rtcp;
using OpenVideoFramework.RtspSource.SDP;

namespace OpenVideoFramework.RtspSource;

internal sealed class TcpRtspTransport : RtspTransport
{
    private readonly Dictionary<TrackReceiver, (byte Rtp, byte Rtcp)> _tracks = [];
    private readonly Dictionary<byte, (TrackReceiver Track, bool IsRtcp)> _channels = [];

    public override TrackReceiver CreateTrack(TrackMetadata metadata)
    {
        for (var channel = 0; channel < 256; channel += 2)
        {
            if (_tracks.Values.Any(pair => pair.Rtp == channel || pair.Rtcp == channel ||
                                           pair.Rtp == channel + 1 || pair.Rtcp == channel + 1))
            { continue; }
            var track = new TrackReceiver(metadata);
            _tracks.Add(track, ((byte)channel, (byte)(channel + 1)));
            return track;
        }

        throw new InvalidOperationException("No free RTSP interleaved channels.");
    }

    public override string GetTransportHeader(TrackReceiver track)
    {
        var channels = _tracks[track];
        return $"Transport: RTP/AVP/TCP;unicast;interleaved={channels.Rtp}-{channels.Rtcp}";
    }

    public override void ConfigureTrack(TrackReceiver track, string transport)
    {
        if (!transport.Split(';')[0].Trim().Equals("RTP/AVP/TCP", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The server did not accept TCP transport.");
        }

        var channels = GetTransportParameter(transport, "interleaved").Split('-');
        if (channels.Length != 2 || !byte.TryParse(channels[0], out var rtp) || !byte.TryParse(channels[1], out var rtcp) ||
            rtp == rtcp || _channels.ContainsKey(rtp) || _channels.ContainsKey(rtcp))
        {
            throw new InvalidDataException("Invalid or overlapping RTSP interleaved channels.");
        }

        _tracks[track] = (rtp, rtcp);
        _channels.Add(rtp, (track, false));
        _channels.Add(rtcp, (track, true));
    }

    protected override ValueTask ReceiveInterleavedAsync(byte channel, byte[] data, CancellationToken token)
    {
        if (!_channels.TryGetValue(channel, out var receiver))
        {
            throw new InvalidDataException($"Unknown RTSP interleaved channel {channel}.");
        }

        if (!receiver.IsRtcp)
        {
            return ReceiveRtpAsync(receiver.Track, data, token);
        }

        ReceiveRtcp(data);
        return ValueTask.CompletedTask;
    }

    public override ValueTask SendReceiverReportAsync(TrackReceiver track, RtcpReceiverReport report, CancellationToken token)
    {
        var data = RtcpSerializer.SerializeReceiverReport(report);
        var packet = new byte[4 + data.Length];
        packet[0] = (byte)'$';
        packet[1] = _tracks[track].Rtcp;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), checked((ushort)data.Length));
        data.CopyTo(packet, 4);
        return WriteAsync(packet, token);
    }
}
