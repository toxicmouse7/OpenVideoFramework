namespace OpenVideoFramework.Common;

public class AudioFrame : CompleteFrame
{
    public required byte[] Data
    {
        get; init;
    }
    public byte[]? ExtraData
    {
        get; init;
    }
    public required bool IsKeyFrame
    {
        get; init;
    }
    public required Codec Codec
    {
        get; init;
    }
    public required TimeSpan Duration
    {
        get; init;
    }
    public int SampleRate
    {
        get; set;
    }
    public int Channels
    {
        get; set;
    }
    public int Bitrate
    {
        get; set;
    }
}
