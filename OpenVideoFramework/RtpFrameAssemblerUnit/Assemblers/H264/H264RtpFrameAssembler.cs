using Microsoft.Extensions.Logging;
using OpenVideoFramework.Common;
using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers.H264;

public class H264RtpFrameAssembler : RtpFrameAssembler
{
    private const byte NalTypeMask = 0x1F;
    private const byte FuStartBit = 0x80;
    private const byte FuEndBit = 0x40;
    private static readonly byte[] StartCode = [0x00, 0x00, 0x00, 0x01];

    private readonly ILogger<H264RtpFrameAssembler> _logger;
    private readonly List<byte[]> _nalUnits = [];
    private uint _currentTimestamp;
    private RtpPacket _lastPacket = null!;
    private bool _frameContainsIdr;
    private bool _frameContainsSps;
    private bool _frameContainsPps;
    private byte[]? _fragmentedNalBuffer;
    private byte[]? _lastSps;
    private byte[]? _lastPps;
    private int _width;
    private int _height;

    public H264RtpFrameAssembler(ILogger<H264RtpFrameAssembler> logger)
    {
        _logger = logger;
    }

    public override CompleteFrame? AddPacket(RtpPacket packet)
    {
        if (packet.Content.Length == 0)
        {
            _logger.LogWarning("Received empty H264 RTP payload.");
            return null;
        }

        if (_nalUnits.Count > 0 && packet.Header.Timestamp != _currentTimestamp)
        {
            _logger.LogWarning("New frame started before previous completed. Dropping incomplete frame.");
            DropFrame();
        }

        _currentTimestamp = packet.Header.Timestamp;
        _lastPacket = packet;

        if (!TryProcessPacket(packet.Content))
        {
            DropFrame();
            return null;
        }

        return packet.Header.Marker ? CreateFrame() : null;
    }

    private bool TryProcessPacket(byte[] payload)
    {
        var nalType = payload[0] & NalTypeMask;

        return nalType switch
        {
            >= 1 and <= 23 => TryProcessSingleNalUnit(payload),
            24 => TryProcessStapA(payload),
            28 => TryProcessFuA(payload),
            _ => LogUnsupportedNalType(nalType)
        };
    }

    private bool TryProcessSingleNalUnit(byte[] payload)
    {
        AppendNalUnit(payload);
        return true;
    }

    private bool TryProcessStapA(byte[] payload)
    {
        var offset = 1;
        while (offset + 2 <= payload.Length)
        {
            var nalSize = (payload[offset] << 8) | payload[offset + 1];
            offset += 2;

            if (nalSize == 0)
            {
                _logger.LogWarning("STAP-A packet contains empty NAL unit.");
                continue;
            }

            if (offset + nalSize > payload.Length)
            {
                _logger.LogError("Invalid STAP-A packet. NAL unit exceeds payload size.");
                return false;
            }

            AppendNalUnit(payload[offset..(offset + nalSize)]);
            offset += nalSize;
        }

        if (offset != payload.Length)
        {
            _logger.LogError("Invalid STAP-A packet. Trailing bytes detected.");
            return false;
        }

        return true;
    }

    private bool TryProcessFuA(byte[] payload)
    {
        if (payload.Length < 2)
        {
            _logger.LogError("Invalid FU-A packet. Payload too short.");
            return false;
        }

        var fuIndicator = payload[0];
        var fuHeader = payload[1];
        var isStart = (fuHeader & FuStartBit) != 0;
        var isEnd = (fuHeader & FuEndBit) != 0;
        var nalHeader = (byte)((fuIndicator & 0xE0) | (fuHeader & NalTypeMask));
        var fragmentPayload = payload[2..];

        if (isStart)
        {
            _fragmentedNalBuffer = [nalHeader, .. fragmentPayload];
            return true;
        }

        if (_fragmentedNalBuffer is null)
        {
            _logger.LogWarning("Received FU-A continuation without start fragment.");
            return false;
        }

        _fragmentedNalBuffer = [.. _fragmentedNalBuffer, .. fragmentPayload];

        if (!isEnd)
        {
            return true;
        }

        AppendNalUnit(_fragmentedNalBuffer);
        _fragmentedNalBuffer = null;
        return true;
    }

    private bool LogUnsupportedNalType(int nalType)
    {
        _logger.LogWarning("Unsupported H264 NAL unit type {NalType}.", nalType);
        return false;
    }

    private void AppendNalUnit(byte[] nalUnit)
    {
        _nalUnits.Add([.. StartCode, .. nalUnit]);

        switch (nalUnit[0] & NalTypeMask)
        {
            case 5:
                _frameContainsIdr = true;
                break;
            case 7:
                _frameContainsSps = true;
                _lastSps = [.. nalUnit];
                TryUpdateDimensions(nalUnit);
                break;
            case 8:
                _frameContainsPps = true;
                _lastPps = [.. nalUnit];
                break;
        }
    }

    private VideoFrame CreateFrame()
    {
        try
        {
            var frameData = AssembleFrameData();
            return new VideoFrame
            {
                Data = frameData,
                IsKeyFrame = _frameContainsIdr,
                ReceivedAt = _lastPacket.ReceivedAt,
                Codec = Codec.H264,
                Duration = TimeSpan.MinValue,
                ClockRate = _lastPacket.ClockRate,
                Width = _width,
                Height = _height
            };
        }
        finally
        {
            DropFrame();
        }
    }

    private byte[] AssembleFrameData()
    {
        var nalUnits = new List<byte[]>();

        if (_frameContainsIdr)
        {
            if (!_frameContainsSps && _lastSps is not null)
            {
                nalUnits.Add([.. StartCode, .. _lastSps]);
            }

            if (!_frameContainsPps && _lastPps is not null)
            {
                nalUnits.Add([.. StartCode, .. _lastPps]);
            }
        }

        nalUnits.AddRange(_nalUnits);
        return nalUnits.SelectMany(static x => x).ToArray();
    }

    private void TryUpdateDimensions(byte[] spsNalUnit)
    {
        try
        {
            if (!TryParseSpsDimensions(spsNalUnit, out var width, out var height))
            {
                return;
            }

            _width = width;
            _height = height;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse H264 SPS dimensions.");
        }
    }

    private void DropFrame()
    {
        _nalUnits.Clear();
        _fragmentedNalBuffer = null;
        _frameContainsIdr = false;
        _frameContainsSps = false;
        _frameContainsPps = false;
    }

    private static bool TryParseSpsDimensions(byte[] spsNalUnit, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (spsNalUnit.Length < 4)
        {
            return false;
        }

        var rbsp = RemoveEmulationPreventionBytes(spsNalUnit[1..]);
        var reader = new H264BitReader(rbsp);

        var profileIdc = reader.ReadBits(8);
        reader.ReadBits(8);
        reader.ReadBits(8);
        reader.ReadUnsignedExpGolomb();

        var chromaFormatIdc = 1;
        if (profileIdc is 100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135)
        {
            chromaFormatIdc = reader.ReadUnsignedExpGolomb();
            if (chromaFormatIdc == 3)
            {
                reader.ReadBit();
            }

            reader.ReadUnsignedExpGolomb();
            reader.ReadUnsignedExpGolomb();
            reader.ReadBit();

            if (reader.ReadBit())
            {
                var scalingListCount = chromaFormatIdc == 3 ? 12 : 8;
                for (var i = 0; i < scalingListCount; i++)
                {
                    if (reader.ReadBit())
                    {
                        SkipScalingList(reader, i < 6 ? 16 : 64);
                    }
                }
            }
        }

        reader.ReadUnsignedExpGolomb();
        var picOrderCntType = reader.ReadUnsignedExpGolomb();
        switch (picOrderCntType)
        {
            case 0:
                reader.ReadUnsignedExpGolomb();
                break;
            case 1:
                reader.ReadBit();
                reader.ReadSignedExpGolomb();
                reader.ReadSignedExpGolomb();
                var count = reader.ReadUnsignedExpGolomb();
                for (var i = 0; i < count; i++)
                {
                    reader.ReadSignedExpGolomb();
                }

                break;
        }

        reader.ReadUnsignedExpGolomb();
        reader.ReadBit();

        var picWidthInMbsMinus1 = reader.ReadUnsignedExpGolomb();
        var picHeightInMapUnitsMinus1 = reader.ReadUnsignedExpGolomb();
        var frameMbsOnlyFlag = reader.ReadBit();
        if (!frameMbsOnlyFlag)
        {
            reader.ReadBit();
        }

        reader.ReadBit();

        var frameCropLeftOffset = 0;
        var frameCropRightOffset = 0;
        var frameCropTopOffset = 0;
        var frameCropBottomOffset = 0;

        if (reader.ReadBit())
        {
            frameCropLeftOffset = reader.ReadUnsignedExpGolomb();
            frameCropRightOffset = reader.ReadUnsignedExpGolomb();
            frameCropTopOffset = reader.ReadUnsignedExpGolomb();
            frameCropBottomOffset = reader.ReadUnsignedExpGolomb();
        }

        width = (picWidthInMbsMinus1 + 1) * 16;
        height = (picHeightInMapUnitsMinus1 + 1) * 16 * (frameMbsOnlyFlag ? 1 : 2);

        var (cropUnitX, cropUnitY) = GetCropUnits(chromaFormatIdc, frameMbsOnlyFlag);
        width -= (frameCropLeftOffset + frameCropRightOffset) * cropUnitX;
        height -= (frameCropTopOffset + frameCropBottomOffset) * cropUnitY;

        return width > 0 && height > 0;
    }

    private static (int cropUnitX, int cropUnitY) GetCropUnits(int chromaFormatIdc, bool frameMbsOnlyFlag)
    {
        return chromaFormatIdc switch
        {
            0 => (1, 2 - (frameMbsOnlyFlag ? 1 : 0)),
            1 => (2, 2 * (2 - (frameMbsOnlyFlag ? 1 : 0))),
            2 => (2, 2 - (frameMbsOnlyFlag ? 1 : 0)),
            3 => (1, 2 - (frameMbsOnlyFlag ? 1 : 0)),
            _ => (1, 2 - (frameMbsOnlyFlag ? 1 : 0))
        };
    }

    private static void SkipScalingList(H264BitReader reader, int size)
    {
        var lastScale = 8;
        var nextScale = 8;

        for (var j = 0; j < size; j++)
        {
            if (nextScale != 0)
            {
                var deltaScale = reader.ReadSignedExpGolomb();
                nextScale = (lastScale + deltaScale + 256) % 256;
            }

            lastScale = nextScale == 0 ? lastScale : nextScale;
        }
    }

    private static byte[] RemoveEmulationPreventionBytes(byte[] data)
    {
        var result = new List<byte>(data.Length);

        for (var i = 0; i < data.Length; i++)
        {
            if (i >= 2 && data[i] == 0x03 && data[i - 1] == 0x00 && data[i - 2] == 0x00)
            {
                continue;
            }

            result.Add(data[i]);
        }

        return result.ToArray();
    }

    private sealed class H264BitReader
    {
        private readonly byte[] _data;
        private int _bitPosition;

        public H264BitReader(byte[] data)
        {
            _data = data;
        }

        public bool ReadBit()
        {
            return ReadBits(1) != 0;
        }

        public int ReadBits(int bitCount)
        {
            var result = 0;

            for (var i = 0; i < bitCount; i++)
            {
                if (_bitPosition >= _data.Length * 8)
                {
                    throw new InvalidOperationException("Unexpected end of H264 bitstream.");
                }

                var byteIndex = _bitPosition / 8;
                var shift = 7 - (_bitPosition % 8);
                result = (result << 1) | ((_data[byteIndex] >> shift) & 0x01);
                _bitPosition++;
            }

            return result;
        }

        public int ReadUnsignedExpGolomb()
        {
            var leadingZeroBits = 0;
            while (!ReadBit())
            {
                leadingZeroBits++;
            }

            var suffix = leadingZeroBits == 0 ? 0 : ReadBits(leadingZeroBits);
            return ((1 << leadingZeroBits) - 1) + suffix;
        }

        public int ReadSignedExpGolomb()
        {
            var codeNum = ReadUnsignedExpGolomb();
            var sign = (codeNum & 1) == 0 ? -1 : 1;
            return sign * ((codeNum + 1) / 2);
        }
    }
}
