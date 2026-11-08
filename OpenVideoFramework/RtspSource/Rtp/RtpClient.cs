using System.Net;
using System.Net.Sockets;
using OpenVideoFramework.RtspSource.SDP;

namespace OpenVideoFramework.RtspSource.Rtp;

internal sealed class RtpClient : IDisposable
{
    private readonly UdpClient _rtpClient;
    private readonly TrackMetadata _trackMetadata;
    private readonly byte[][]? _h264ParameterSets;

    public RtpClient(TrackMetadata trackMetadata, AddressFamily addressFamily)
    {
        _trackMetadata = trackMetadata;
        _h264ParameterSets = trackMetadata.PayloadType == PayloadType.H264
            ? ParseH264ParameterSets(trackMetadata.FormatParameters)
            : null;
        var bindAddress = addressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;
        _rtpClient = new UdpClient(new IPEndPoint(bindAddress, 0));
    }

    public void Connect(IPEndPoint endPoint) => _rtpClient.Connect(endPoint);

    public async Task<RtpPacket> GetPacketAsync(CancellationToken token = default)
    {
        var udpPacket = await _rtpClient.ReceiveAsync(token);

        var rtpPacket = RtpSerializer.DeserializeRtpPacket(
            udpPacket.Buffer,
            _trackMetadata.PayloadType,
            _h264ParameterSets);

        return rtpPacket;
    }

    private static byte[][]? ParseH264ParameterSets(string? formatParameters)
    {
        if (string.IsNullOrWhiteSpace(formatParameters))
        {
            return null;
        }

        // FormatParameters starts with the SDP payload number followed by its attributes.
        var attributes = formatParameters[(formatParameters.IndexOf(' ') + 1)..];
        const string ParameterName = "sprop-parameter-sets=";
        foreach (var attribute in attributes.Split(';', StringSplitOptions.TrimEntries))
        {
            if (!attribute.StartsWith(ParameterName, StringComparison.Ordinal))
            {
                continue;
            }

            var parameterSets = attribute[ParameterName.Length..].Split(',')
                .Select(Convert.FromBase64String).ToArray();
            foreach (var nalUnit in parameterSets)
            {
                if (nalUnit.Length == 0 || (nalUnit[0] & 0x1F) is not 7 and not 8)
                {
                    throw new FormatException("SDP sprop-parameter-sets must contain H264 SPS/PPS NAL units.");
                }
            }
            return parameterSets;
        }
        return null;
    }

    public int Port => ((IPEndPoint)_rtpClient.Client.LocalEndPoint!).Port;

    public void Dispose() => _rtpClient.Dispose();
}
