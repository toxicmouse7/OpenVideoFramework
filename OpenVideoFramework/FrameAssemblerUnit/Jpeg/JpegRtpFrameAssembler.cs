using OpenVideoFramework.RtpDemuxUnit;

namespace OpenVideoFramework.FrameAssemblerUnit.Jpeg;

public class JpegRtpFrameAssembler : RtpFrameAssembler
{
    private readonly List<byte[]> _jpegFragments = [];
    private uint _currentTimestamp;
    private RtpJpegHeader _lastHeader;

    public override CompleteFrame? AddPacket(DemuxedData demuxedData)
    {
        var header = RtpJpegHeader.Deserialize(demuxedData.Payload);

        if (header.FragmentOffset > _jpegFragments.Sum(x  => x.Length))
        {
            DropFrame();
            return null;
        }
        
        _lastHeader = header;
        
        if (_jpegFragments.Count > 0 && demuxedData.Timestamp != _currentTimestamp)
        {
            return CreateFrame();
        }
        
        _currentTimestamp = demuxedData.Timestamp;
        _jpegFragments.Add(demuxedData.Payload[RtpJpegHeader.Size..]);

        return demuxedData.IsMarker ? CreateFrame() : null;
    }

    private CompleteFrame? CreateFrame()
    {
        if (_jpegFragments.Count == 0)
            return null;
        
        var jpegData = _jpegFragments.SelectMany(x => x).ToArray();
        _jpegFragments.Clear();

        return new CompleteFrame
        {
            Data = jpegData,
            IsKeyFrame = true,
            Width = _lastHeader.Width * 8,
            Height = _lastHeader.Height * 8
        };
    }

    private void DropFrame()
    {
        _lastHeader = default;
        _jpegFragments.Clear();
    }
}