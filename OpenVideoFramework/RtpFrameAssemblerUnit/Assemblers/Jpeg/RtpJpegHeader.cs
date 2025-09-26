using System.Net;

namespace OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers.Jpeg;

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
        Span<byte> offsetBuffer = stackalloc byte[4];
        offsetBuffer[0] = 0;
        data[1..4].CopyTo(offsetBuffer[1..]);
        
        var fragmentOffset = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(offsetBuffer));
        
        return new RtpJpegHeader
        (
            data[0],
            fragmentOffset,
            data[4],
            data[5],
            data[6],
            data[7]
        );
    }
}