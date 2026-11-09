namespace OpenVideoFramework.RtspSource.Rtp;

public class RtpPacket
{
    public RtpPacketHeader Header
    {
        get; init;
    }
    public byte[] Content { get; init; } = null!;
    /// <summary>Raw extension data, excluding the profile and length fields.</summary>
    public ReadOnlyMemory<byte> HeaderExtensionData { get; init; }
    internal IReadOnlyList<byte[]>? H264ParameterSets { get; init; }
    public DateTimeOffset ReceivedAt { get; } = DateTimeOffset.Now;
}
