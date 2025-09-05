using System.Net;

namespace OpenVideoFramework.FrameAssemblerUnit.Jpeg;

public record struct RtpJpegHeader(
    byte TypeSpecific,
    int FragmentOffset,
    byte Type,
    byte Quantization,
    byte Width,
    byte Height)
{
    public const int Size = 8;
    
    public static RtpJpegHeader Deserialize(Span<byte> data)
    {
        return new RtpJpegHeader
        (
            data[0],
            IPAddress.NetworkToHostOrder(BitConverter.ToInt32([..data[1..4], 0])),
            data[4],
            data[5],
            data[6],
            data[7]
        );
    }
}