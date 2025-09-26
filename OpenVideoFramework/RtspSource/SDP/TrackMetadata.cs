using OpenVideoFramework.Common;

namespace OpenVideoFramework.RtspSource.SDP;

public class TrackMetadata
{
    public MediaType MediaType { get; set; }
    public byte PayloadType { get; set; }
    public string Codec { get; set; } = null!;
    public uint ClockRate { get; set; }
    public string FormatParameters { get; set; } = null!;
    public string Prefix { get; set; } = null!;
}