using OpenVideoFramework.RtspSource.Rtcp;
using OpenVideoFramework.RtspSource.Rtp;
using OpenVideoFramework.RtspSource.SDP;

namespace OpenVideoFramework.RtspSource;

public class TrackReceiver
{
    public RtpClient RtpClient { get; set; } = null!;
    public RtcpClient RtcpClient { get; set; } = null!;
    public TrackMetadata Metadata { get; set; } = null!;
}