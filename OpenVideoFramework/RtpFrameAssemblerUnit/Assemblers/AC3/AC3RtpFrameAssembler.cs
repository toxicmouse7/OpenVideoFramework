using Microsoft.Extensions.Logging;
using OpenVideoFramework.Common;
using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers.AC3;

public class AC3RtpFrameAssembler : RtpFrameAssembler
{
    private const ushort AC3SyncWord = 0x0B77;
    private const int SamplesPerFrame = 1536;
    
    private static readonly int[] BitrateTable =
    [
        32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 448, 512, 576, 640, 768, 896,
        1024, 1152, 1280, 1408, 1536, 1664, 1792, 1920, 2048, 2176, 2304, 2432, 2560, 2688, 2816, 2944, 3072
    ];

    private readonly ILogger<AC3RtpFrameAssembler> _logger;
    private readonly List<AC3Fragment> _fragments = [];
    private uint _currentTimestamp;
    private RtpPacket _lastPacket = null!;

    public AC3RtpFrameAssembler(ILogger<AC3RtpFrameAssembler> logger)
    {
        _logger = logger;
    }

    private class AC3Fragment
    {
        public byte[] Data { get; set; } = [];
        public int NumberOfFrames { get; set; }
        public FrameType FrameType { get; set; }
    }

    public override CompleteFrame? AddPacket(RtpPacket packet)
    {
        if (_fragments.Count > 0 && packet.Header.Timestamp != _currentTimestamp)
        {
            _logger.LogWarning("New frame started before previous completed. Dropping incomplete frame.");
            DropFrame();
        }

        _currentTimestamp = packet.Header.Timestamp;
        _lastPacket = packet;

        var header = RtpAc3Header.Deserialize(packet.Content);
        if (header.FrameType == FrameType.NotInitialFragment && _fragments.Count == 0)
        {
            _logger.LogWarning("Received frame without initial fragment. Dropping incomplete frame.");
            return null;
        }

        var fragment = new AC3Fragment
        {
            Data = packet.Content[RtpAc3Header.Size..],
            NumberOfFrames = header.NumberOfFrames,
            FrameType = header.FrameType
        };

        _fragments.Add(fragment);

        if (packet.Header.Marker && _fragments.Count != header.NumberOfFrames)
        {
            _logger.LogError("Missed {count} packets. Dropping frame.", header.NumberOfFrames - _fragments.Count);
            DropFrame();
            return null;
        }

        return packet.Header.Marker ? CreateFrame() : null;
    }

    private AudioFrame? CreateFrame()
    {
        var frameData = GetAssembledData();

        if (!IsValidAC3Frame(frameData))
        {
            _logger.LogError("Invalid AC3 frame. Dropping frame.");
            DropFrame();
            return null;
        }

        var frameInfo = ParseAC3Header(frameData);

        var audioFrame = new AudioFrame
        {
            Data = frameData,
            ReceivedAt = _lastPacket.ReceivedAt,
            IsKeyFrame = true,
            Codec = Codec.AC3,
            Duration = TimeSpan.FromSeconds(SamplesPerFrame / (double)frameInfo.SampleRate),
            ClockRate = _lastPacket.ClockRate,
            SampleRate = frameInfo.SampleRate,
            Channels = frameInfo.Channels,
            Bitrate = frameInfo.Bitrate,
        };

        DropFrame();

        return audioFrame;
    }

    private byte[] GetAssembledData()
    {
        return _fragments.SelectMany(f => f.Data).ToArray();
    }

    private bool IsValidAC3Frame(byte[] data)
    {
        if (data.Length < 2) return false;

        var syncWord = (ushort)((data[0] << 8) | data[1]);
        return syncWord == AC3SyncWord;
    }

    private AC3FrameInfo ParseAC3Header(byte[] data)
    {
        var info = new AC3FrameInfo();

        if (data.Length < 6)
            return info;

        try
        {
            // Sample rate
            var fscod = (data[4] >> 6) & 0x3;
            info.SampleRate = fscod switch
            {
                0 => 48000,
                1 => 44100,
                2 => 32000,
                _ => 0
            };

            // Frame size and bitrate
            var frmsizecod = data[4] & 0x3F;
            if (frmsizecod < 38)
            {
                info.Bitrate = BitrateTable[frmsizecod];
            }

            // Audio coding mode (channels)
            if (data.Length > 6)
            {
                var acmod = (data[6] >> 5) & 0x7;
                info.Channels = acmod switch
                {
                    0 => 2, // 1+1 (dual mono)
                    1 => 1, // 1/0 (mono)
                    2 => 2, // 2/0 (stereo)
                    3 => 3, // 3/0
                    4 => 3, // 2/1
                    5 => 4, // 3/1
                    6 => 4, // 2/2
                    7 => 5, // 3/2
                    _ => 2
                };

                // LFE channel
                var lfeon = (data[6] >> 4) & 0x1;
                if (lfeon == 1)
                {
                    info.Channels++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing AC-3 header");
        }

        return info;
    }

    private void DropFrame()
    {
        _fragments.Clear();
    }

    private class AC3FrameInfo
    {
        public int SampleRate { get; set; }
        public int Channels { get; set; } = 2;
        public int Bitrate { get; set; }
    }
}