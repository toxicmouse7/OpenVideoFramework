using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers;

public abstract class RtpFrameAssembler
{
    public abstract CompleteFrame? AddPacket(RtpPacket packet);
}