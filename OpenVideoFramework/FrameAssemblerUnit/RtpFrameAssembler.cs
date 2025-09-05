using OpenVideoFramework.RtpDemuxUnit;

namespace OpenVideoFramework.FrameAssemblerUnit;

public abstract class RtpFrameAssembler
{
    public abstract CompleteFrame? AddPacket(DemuxedData demuxedData);
}