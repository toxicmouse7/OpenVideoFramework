namespace OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers.AC3;

public class RtpAc3Header
{
    public const int Size = 2;
    
    public byte MustBeZero { get; init; }
    public FrameType FrameType { get; init; }
    public byte NumberOfFrames { get; init; }

    public static RtpAc3Header Deserialize(byte[] data)
    {
        return new RtpAc3Header
        {
            MustBeZero = (byte)(data[0] >> 2),
            FrameType = (FrameType)(data[0] & 0x3),
            NumberOfFrames = data[1]
        };
    }
}

public enum FrameType
{
    CompleteFrame = 0,
    InitialFragmentWithHeader = 1,
    InitialFragmentWithoutHeader = 2,
    NotInitialFragment = 3
}