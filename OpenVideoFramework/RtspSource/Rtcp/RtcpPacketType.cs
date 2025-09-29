namespace OpenVideoFramework.RtspSource.Rtcp;

internal enum RtcpPacketType : byte
{
    SenderReport = 200,
    ReceiverReport = 201,
    SourceDescription = 202,
    Goodbye = 203,
    ApplicationDefined = 204
}