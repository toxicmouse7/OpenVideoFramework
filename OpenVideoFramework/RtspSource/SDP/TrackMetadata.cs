using OpenVideoFramework.Common;
using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtspSource.SDP;

public class TrackMetadata
{
    public MediaType MediaType { get; set; }
    public PayloadType PayloadType { get; set; }
    public uint ClockRate { get; set; }
    public string FormatParameters { get; set; } = null!;
    public string Prefix { get; set; } = null!;
}