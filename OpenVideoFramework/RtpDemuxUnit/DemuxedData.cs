namespace OpenVideoFramework.RtpDemuxUnit;

public class DemuxedData
{
    public uint Ssrc { get; set; }           // Идентификатор потока
    public byte PayloadType { get; set; }    // Тип полезной нагрузки (96=H264, 26=JPEG)
    public uint Timestamp { get; set; }      // Временная метка RTP
    public ushort SequenceNumber { get; set; }// Порядковый номер
    public byte[] Payload { get; set; } = null!;      // Полезная нагрузка
    public bool IsMarker { get; set; }       // Маркер конца фрейма
    public MediaStreamContext StreamContext { get; init; } = null!;
}