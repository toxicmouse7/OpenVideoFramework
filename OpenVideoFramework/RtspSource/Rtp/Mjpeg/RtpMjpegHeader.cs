namespace OpenVideoFramework.RtspSource.Rtp.Mjpeg;

public record struct RtpMjpegHeader(
    byte TypeSpecific,
    byte[] FragmentOffset,
    byte Type,
    byte Quantization,
    byte Width,
    byte Height)
{
    public static RtpMjpegHeader Deserialize(Span<byte> data)
    {
        return new RtpMjpegHeader
        (
            data[0],
            data[1..4].ToArray(),
            data[4],
            data[5],
            data[6],
            data[7]
        );
    }
}