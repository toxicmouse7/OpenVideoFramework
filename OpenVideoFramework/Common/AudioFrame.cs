namespace OpenVideoFramework.Common;

public class AudioFrame : CompleteFrame
{
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int Bitrate { get; set; }
}