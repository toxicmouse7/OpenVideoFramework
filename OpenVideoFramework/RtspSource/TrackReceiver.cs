using System.Diagnostics;
using OpenVideoFramework.RtspSource.Rtp;
using OpenVideoFramework.RtspSource.SDP;

namespace OpenVideoFramework.RtspSource;

internal sealed class TrackReceiver
{
    public TrackMetadata Metadata { get; }
    public IReadOnlyList<byte[]>? H264ParameterSets { get; }
    public Stopwatch ReportTimer { get; } = Stopwatch.StartNew();

    public TrackReceiver(TrackMetadata metadata)
    {
        Metadata = metadata;
        H264ParameterSets = metadata.PayloadType == PayloadType.H264
            ? ParseH264ParameterSets(metadata.FormatParameters)
            : null;
    }

    private static byte[][]? ParseH264ParameterSets(string? formatParameters)
    {
        if (string.IsNullOrWhiteSpace(formatParameters))
        { return null; }
        var attributes = formatParameters[(formatParameters.IndexOf(' ') + 1)..];
        const string ParameterName = "sprop-parameter-sets=";
        foreach (var attribute in attributes.Split(';', StringSplitOptions.TrimEntries))
        {
            if (!attribute.StartsWith(ParameterName, StringComparison.Ordinal))
            { continue; }
            var parameterSets = attribute[ParameterName.Length..].Split(',').Select(Convert.FromBase64String).ToArray();
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
}
