using OpenVideoFramework.Common;

namespace OpenVideoFramework.RtspSource.SDP;

public class SdpParser
{
    public static List<TrackMetadata> Parse(string sdpContent)
    {
        var metadataList = new List<TrackMetadata>();
        var lines = sdpContent.Split('\n');

        TrackMetadata currentMetadata = null!;

        foreach (var line in lines)
        {
            if (line.StartsWith("m="))
            {
                currentMetadata = new TrackMetadata();
                var parts = line.Split(' ');
                currentMetadata.MediaType = parts[0] == "m=video" ? MediaType.Video : MediaType.Audio;
                currentMetadata.PayloadType = byte.Parse(parts[3]);
                metadataList.Add(currentMetadata);
            }
            else if (line.StartsWith("a=rtpmap:"))
            {
                var parts = line.Split(' ');
                if (parts.Length > 1 && currentMetadata != null)
                {
                    var codecParts = parts[1].Split('/');
                    currentMetadata.Codec = codecParts[0];
                    if (codecParts.Length > 1)
                        currentMetadata.ClockRate = uint.Parse(codecParts[1]);
                }
            }
            else if (line.StartsWith("a=fmtp:"))
            {
                if (currentMetadata != null)
                {
                    currentMetadata.FormatParameters = line[7..];
                }
            }
            else if (line.StartsWith("a=control:"))
            {
                if (currentMetadata is not null)
                {
                    var uri = new Uri(line[10..].TrimEnd('\r'));
                    currentMetadata.Prefix = uri.PathAndQuery;
                }
            }
        }

        return metadataList;
    }
}