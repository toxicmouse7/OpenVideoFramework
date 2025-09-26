namespace OpenVideoFramework.RtspSource.Rtcp;

public enum RtcpPacketType : byte
{
    SenderReport = 200,
    ReceiverReport = 201,
    SourceDescription = 202,
    Goodbye = 203,
    ApplicationDefined = 204
}